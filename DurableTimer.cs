using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Durable.Timer.Microservice
{
    public static class DurableTimer
    {
        [Deterministic]
        [FunctionName("TimerOrchestrator")]
        public static async Task TimerOrchestratorForStatusReturn(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger logger)
        {
            await OrchestrateTimer(context, logger);
        }

        private static async Task OrchestrateTimer(IDurableOrchestrationContext context, ILogger logger)
        {
            ILogger slog = context.CreateReplaySafeLogger(logger);

            string postData = context.GetInput<string>();

            (TimerObject timerObject, bool isDurableCheck) = JsonConvert.DeserializeObject<(TimerObject, bool)>(postData);

            int count = 0;

            HttpRetryOptions ret = new(TimeSpan.FromSeconds(10), 5)
            {
                BackoffCoefficient = 1.5,
                MaxRetryInterval = TimeSpan.FromMinutes(30),
                RetryTimeout = TimeSpan.FromMinutes(300)
            };

            DateTime deadline;

            if (timerObject.RetryOptions.StartDelayMinutes > 0)
            {
                deadline = context.CurrentUtcDateTime.AddSeconds(timerObject.RetryOptions.StartDelayMinutes);

                await context.CreateTimer(deadline, default);

                try
                {
                    slog.LogCritical("Executing timer...");

                    if (!await ExecuteTimer(context, timerObject, ret, isDurableCheck))
                    {
                        slog.LogWarning("Timer is done.");

                        return;
                    }
                }
                catch (HttpRequestException ex)
                {
                    slog.LogError("Call failed with a :" + ex.StatusCode);

                    return;
                }

                count++;
            }

            while (count < timerObject.RetryOptions.MaxRetries)
            {
                deadline = context.CurrentUtcDateTime.AddSeconds(timerObject.RetryOptions.DelayMinutes);

                await context.CreateTimer(deadline, default);

                try
                {
                    slog.LogCritical("Executing timer...");

                    if (!await ExecuteTimer(context, timerObject, ret, isDurableCheck))
                    {
                        slog.LogWarning("Timer is done.");

                        return;
                    }
                }
                catch (HttpRequestException ex)
                {
                    slog.LogError("Call failed with a :" + ex.StatusCode);

                    return;
                }

                count++;
            }

            slog.LogWarning("Timer is done.");

            return;
        }

        private static async Task<bool> ExecuteTimer(IDurableOrchestrationContext context, TimerObject timerObject, HttpRetryOptions ret, bool isDurableCheck)
        {

            DurableHttpResponse statusResponse = await context.CallHttpAsync(new DurableHttpRequest(HttpMethod.Get, new Uri(timerObject.StatusCheckUrl), asynchronousPatternEnabled: false, httpRetryOptions: ret));
            
            if (statusResponse.StatusCode == HttpStatusCode.Accepted)
            {
                return true;
            }

            if (isDurableCheck)
            {
                if (statusResponse.StatusCode == HttpStatusCode.OK)
                {
                    RuntimeStatus runtimeStatus = JsonConvert.DeserializeObject<RuntimeStatus>(statusResponse.Content);

                    if (runtimeStatus.Equals("Running"))
                    {
                        await context.CallHttpAsync(new DurableHttpRequest(HttpMethod.Post, new Uri(timerObject.ActionUrl), content: timerObject.Content, httpRetryOptions: ret));

                        return true;
                    }
                    
                    if(runtimeStatus.Equals("Pending"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        //[FunctionName("TimerOrchestratorForBoolReturn")]
        //public static async Task<bool> TimerOrchestratorForBoolReturn(
        //    [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger logger)
        //{
        //    ILogger slog = context.CreateReplaySafeLogger(logger);

        //    string postData = context.GetInput<string>();

        //    TimerObject timerObject = JsonSerializer.Deserialize<TimerObject>(postData);

        //    DateTime deadline = context.CurrentUtcDateTime.AddSeconds(timerObject.RetryOptions.DelayMinutes);

        //    await context.CreateTimer(deadline, default);

        //    try
        //    {
        //        slog.LogWarning("Trying the call...");

        //        DurableHttpResponse response = await context.CallHttpAsync(new DurableHttpRequest(HttpMethod.Post, new Uri(timerObject.StatusCheckUrl), content: timerObject.Content, httpRetryOptions: ret));
        //    }
        //    catch (HttpRequestException ex)
        //    {
        //        slog.LogError("Call failed with a :" + ex.StatusCode);

        //        return false;
        //    }

        //    return true;
        //}

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
                instanceId = await starter.StartNewAsync("TimerOrchestrator", null, JsonConvert.SerializeObject((new TimerObject()
                {
                    Content = "wappa",
                    StatusCheckUrl = "http://localhost:7072/runtime/webhooks/durabletask/instances/timer_qwerty?taskHub=TestHubName&connection=Storage&code=p_kRXsa9EfdXgtRn3cH76l1K-cBzsuNIUz6EKHgtwdHDAzFuaoJ8Fw==",
                    ActionUrl = "https://reqbin.com/ecfho/post/json",
                    RetryOptions = new()
                    {
                        //BackoffCoefficient = 1,
                        DelayMinutes = 5,
                        //MaxDelayMinutes = 10,
                        MaxRetries = 50
                    }
                }, true)));
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

    public class RuntimeStatus
    {
        public string runtimeStatus { get; set; }
    }

    public class TimerObject
    {
        public RetryOptions RetryOptions { get; set; }
        public string Content { get; set; }
        public string StatusCheckUrl { get; set; }
        public string ActionUrl { get; set; }
    }

    public class RetryOptions
    {
        public int DelayMinutes { get; set; } = 5;
        //public int MaxDelayMinutes { get; set; } = 25;
        public int StartDelayMinutes { get; set; }
        public int MaxRetries { get; set; } = 5;
        //public double BackoffCoefficient { get; set; } = 1;
        //public int TimeOutSeconds { get; set; }
    }
}