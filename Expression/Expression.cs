﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Expression.Interfaces;
using System;

namespace Expression
{
    /// <remarks>
    /// This class represents an actor.
    /// Every ActorID maps to an instance of this class.
    /// The StatePersistence attribute determines persistence and replication of actor state:
    ///  - Persisted: State is written to disk and replicated.
    ///  - Volatile: State is kept in memory only and replicated.
    ///  - None: State is kept in memory only and not replicated.
    /// </remarks>
    [StatePersistence(StatePersistence.Persisted)]
    internal class Expression : Actor, IExpression, IRemindable
    {
        /// <summary>
        /// Initializes a new instance of Expression
        /// </summary>
        /// <param name="actorService">The Microsoft.ServiceFabric.Actors.Runtime.ActorService that will host this actor instance.</param>
        /// <param name="actorId">The Microsoft.ServiceFabric.Actors.ActorId for this actor instance.</param>
        public Expression(ActorService actorService, ActorId actorId)
            : base(actorService, actorId)
        {
        }

        /// <summary>
        /// This method is called whenever an actor is activated.
        /// An actor is activated the first time any of its methods are invoked.
        /// </summary>
        protected override Task OnActivateAsync()
        {
            ActorEventSource.Current.ActorMessage(this, "Actor activated.");
            return Task.CompletedTask;
        }

        public async Task ExtractVariablesAsync(string expression)
        {
            await RegisterReminderAsync(expression, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(-1));
        }

        public async Task ReceiveReminderAsync(string expression, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            var parts = await GetExpressionPartsAsync(expression);
            var variables = parts.Where(x => x.Type == Interfaces.Type.Variable).ToArray();
            this.GetEvent<IExpressionEvents>().ProcessCompleted(expression, variables);
        }

        private async Task<IEnumerable<ExpressionPart>> GetExpressionPartsAsync(string expression, int startIdex = 0)
        {
            var builder = new ExpressionPartBuilder();
            var result = new List<ExpressionPart>();

            if (startIdex == 0)
                SendProgress(0, expression);

            for (var i = 0; i < expression.Length; i++)
            {
                var ch = expression[i];

                if (ch == '(')
                {
                    var endIndex = expression.IndexOf(')', i);
                    var innerParts = await GetExpressionPartsAsync(expression.Substring(i + 1, endIndex - i), i + 1);

                    result.Add(builder.BuildFunction(startIdex + i));
                    result.AddRange(innerParts);

                    i = ++endIndex;
                }
                else if (ch == '"' || ch == '.' || ch == '_' || char.IsNumber(ch) || char.IsLetter(ch))
                {
                    builder.Append(ch);
                }
                else if (builder.HasValue())
                {
                    result.Add(builder.Build(startIdex + i));
                }

                await Task.Delay(500);

                if (startIdex == 0)
                    SendProgress(i, expression);
            }

            if (builder.HasValue())
            {
                result.Add(builder.Build(startIdex + expression.Length));
            }

            SendProgress(startIdex + expression.Length, expression);
            return result;
        }

        private void SendProgress(int index, string expression)
        {
            this.GetEvent<IExpressionEvents>().ProgressUpdated(expression, 100m * index / expression.Length);
        }
    }
}
