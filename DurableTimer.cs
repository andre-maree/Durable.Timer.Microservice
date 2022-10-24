using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
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

            int count = 1;

            HttpRetryOptions ret = new(TimeSpan.FromSeconds(10), 5)
            {
                BackoffCoefficient = 1.2,
                MaxRetryInterval = TimeSpan.FromMinutes(30),
                //RetryTimeout = TimeSpan.FromMinutes(300)
            };

            DateTime deadline = context.CurrentUtcDateTime.AddSeconds(
                timerObject.RetryOptions.StartDelaySeconds > 0
                ? timerObject.RetryOptions.StartDelaySeconds
                : timerObject.RetryOptions.DelaySeconds);

            await context.CreateTimer(deadline, default);

            try
            {
                slog.LogCritical("Executing timer... " + context.CurrentUtcDateTime);

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

            while (count < timerObject.RetryOptions.MaxRetries)
            {
                deadline = context.CurrentUtcDateTime.AddSeconds(timerObject.RetryOptions.DelaySeconds * Math.Pow(count, timerObject.RetryOptions.BackoffCoefficient));

                await context.CreateTimer(deadline, default);

                try
                {
                    slog.LogCritical("Executing timer... " + context.CurrentUtcDateTime);

                    if (!await ExecuteTimer(context, timerObject, ret, isDurableCheck))
                    {
                        slog.LogWarning("Timer is done.");

                        return;
                    }
                }
                catch (HttpRequestException ex)
                {
                    slog.LogError("Call failed with: " + ex.StatusCode);

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
                    Status runtimeStatus = JsonConvert.DeserializeObject<Status>(statusResponse.Content);

                    if (runtimeStatus.RuntimeStatus.Equals("Running"))
                    {
                        await context.CallHttpAsync(new DurableHttpRequest(HttpMethod.Post, new Uri(timerObject.ActionUrl), content: timerObject.Content, httpRetryOptions: ret));

                        return true;
                    }

                    if (runtimeStatus.Equals("Pending"))
                    {
                        return true;
                    }
                }
            }
            else if (statusResponse.StatusCode == HttpStatusCode.OK)
            {
                await context.CallHttpAsync(new DurableHttpRequest(HttpMethod.Post, new Uri(timerObject.ActionUrl), content: timerObject.Content, httpRetryOptions: ret));

                return true;
            }

            return false;
        }

        [FunctionName("SetTimerForDurableFunctionCheck")]
        public static async Task<HttpResponseMessage> SetTimerForDurableFunctionCheck(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "SetTimerForDurableFunctionCheck")
            ] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            string instanceId = Guid.NewGuid().ToString();

            //if (req.Method == HttpMethod.Get)
            //{
            //    await starter.StartNewAsync("TimerOrchestrator", instanceId, JsonConvert.SerializeObject((new TimerObject()
            //    {
            //        Content = "wappa",
            //        StatusCheckUrl = "http://localhost:7072/runtime/webhooks/durabletask/instances/timer_qwerty?taskHub=TestHubName&connection=Storage&code=p_kRXsa9EfdXgtRn3cH76l1K-cBzsuNIUz6EKHgtwdHDAzFuaoJ8Fw==",
            //        ActionUrl = "https://reqbin.com/ecfho/post/json",
            //        RetryOptions = new()
            //        {
            //            StartDelaySeconds = 15,
            //            BackoffCoefficient = 1.2,
            //            DelaySeconds = 5,
            //            MaxDelaySeconds = 100,
            //            MaxRetries = 1
            //        }
            //    }, true)));
            //}
            //else
            //{
            await starter.StartNewAsync("TimerOrchestrator", instanceId, (await req.Content.ReadAsStringAsync(), true));
            //}

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName("SetTimerForApiCallCheck")]
        public static async Task<HttpResponseMessage> SetTimerForApiCallCheck(
                [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "SetTimerForApiCallCheck")
            ] HttpRequestMessage req,
                [DurableClient] IDurableOrchestrationClient starter,
                ILogger log)
        {
            string instanceId = Guid.NewGuid().ToString();

            //if (req.Method == HttpMethod.Get)
            //{
            //    await starter.StartNewAsync("TimerOrchestrator", instanceId, JsonConvert.SerializeObject((new TimerObject()
            //    {
            //        Content = "wappa",
            //        StatusCheckUrl = "http://localhost:7072/runtime/webhooks/durabletask/instances/timer_qwerty?taskHub=TestHubName&connection=Storage&code=p_kRXsa9EfdXgtRn3cH76l1K-cBzsuNIUz6EKHgtwdHDAzFuaoJ8Fw==",
            //        ActionUrl = "https://reqbin.com/ecfho/post/json",
            //        RetryOptions = new()
            //        {
            //            StartDelaySeconds = 15,
            //            BackoffCoefficient = 1.2,
            //            DelaySeconds = 5,
            //            MaxDelaySeconds = 100,
            //            MaxRetries = 1
            //        }
            //    }, false)));
            //}
            //else
            //{
                await starter.StartNewAsync("TimerOrchestrator", instanceId, (await req.Content.ReadAsStringAsync(), false));
            //}

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}