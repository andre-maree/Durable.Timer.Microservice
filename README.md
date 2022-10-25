# Durable.Timer.Microservice

- Set a recurring timer with an API call.
- Set a status check API URL to check if the timer should complete or continue polling.
- Set an action API URL for every occurrence that the timer executes.
- Set a start delay interval and a reccurring interval with an exponential backoff.
- Built in support for a Durable Function status check.

Note: To easily test this service, use the class OrchestrationStateSimulations.cs in the MicroserviceEmulator.sln which is included in the Microflow repo:
https://github.com/andre-maree/Microflow/tree/master/MicroserviceEmulator

Example use case:

A Durable Function orchestration has spawned a webhook that is waiting for a human action. Use Durable.Timer.Microservice to check the status (status check URL) of the orchestration and send a reminder (action URL) to the person that an action is needed (orchestration still in running state).

Set timer API:
```r
SetTimerForApiCallCheck: [POST] SetTimerForApiCallCheck
SetTimerForDurableFunctionCheck: [POST] SetTimerForDurableFunctionCheck
```

The timer model classes:
```csharp
public class TimerObject
{
    public RetryOptions RetryOptions { get; set; }
    public string Content { get; set; }
    public string StatusCheckUrl { get; set; }
    public string ActionUrl { get; set; }
}

public class RetryOptions
{
    public double DelaySeconds { get; set; } = 5;
    public double MaxDelaySeconds { get; set; } = 60;
    public double StartDelaySeconds { get; set; }
    public double MaxRetries { get; set; } = 10;
    public double BackoffCoefficient { get; set; } = 1.2;
}
```
