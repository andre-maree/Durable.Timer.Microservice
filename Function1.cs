using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace Durable.Timer.Microservice
{
    public static class Function1
    {
        [FunctionName("TimerOrchestrator")]
        public static async Task<List<string>> TimerOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            string postData = context.GetInput<string>();

            TimerObject timerObject = JsonSerializer.Deserialize<TimerObject>(postData);

            int count = 0;

            //while (count < timerObject.MaxUccurrences)
            //{
            DateTime deadline = context.CurrentUtcDateTime.AddSeconds(timerObject.RetryOptions.DelayMinutes);

            await context.CreateTimer(deadline, default);

            HttpRetryOptions ret = new(TimeSpan.FromSeconds(timerObject.RetryOptions.DelayMinutes), timerObject.RetryOptions.MaxRetries)
            {
                BackoffCoefficient = timerObject.RetryOptions.BackoffCoefficient,
                //MaxRetryInterval = TimeSpan.FromMinutes(timerObject.RetryOptions.MaxDelayMinutes)
            };

            try
            {
                DurableHttpResponse response = await context.CallHttpAsync(new DurableHttpRequest(HttpMethod.Post, new Uri(timerObject.URL), content: timerObject.Content, httpRetryOptions: ret));
            }
            catch(HttpRequestException ex)
            {
                var r = 0;
            }

            //    count++;
            //}

            List<string> outputs = new()
            {
                // Replace "hello" with the name of your Durable Activity Function.
                await context.CallActivityAsync<string>(nameof(SayHello), "Tokyo"),
                await context.CallActivityAsync<string>(nameof(SayHello), "Seattle"),
                await context.CallActivityAsync<string>(nameof(SayHello), "London")
            };

            // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]
            return outputs;
        }

        [FunctionName(nameof(SayHello))]
        public static string SayHello([ActivityTrigger] string name, ILogger log)
        {
            log.LogInformation($"Saying hello to {name}.");
            return $"Hello {name}!";
        }

        [FunctionName("SetTimer")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", Route = "SetTimer")
            ] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            string instanceId;

            if (req.Method == HttpMethod.Get)
            {
                instanceId = await starter.StartNewAsync("TimerOrchestrator", null, JsonSerializer.Serialize(new TimerObject()
                {
                    Content = "wappa",
                    URL = "https://reqbin.com/echo/post/json",
                    RetryOptions = new()
                    {
                        BackoffCoefficient = 1,
                        DelayMinutes = 5,
                        MaxDelayMinutes = 10,
                        TimeOutSeconds = 1000,
                        MaxRetries = 2
                    }
                }));
            }
            else
            {
                // Function input comes from the request content.
                instanceId = await starter.StartNewAsync("TimerOrchestrator", null, await req.Content.ReadAsStringAsync());
            }

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }

    public class TimerObject
    {
        public RetryOptions RetryOptions { get; set; }
        public string Content { get; set; }
        public string URL { get; set; }
    }

    public class RetryOptions
    {
        public int DelayMinutes { get; set; } = 5;
        public int MaxDelayMinutes { get; set; } = 120;
        public int MaxRetries { get; set; } = 15;
        public double BackoffCoefficient { get; set; } = 5;
        public int TimeOutSeconds { get; set; } = 300;
    }
}