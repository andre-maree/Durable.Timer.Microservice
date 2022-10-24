namespace Durable.Timer.Microservice
{
    public class Status
    {
        public string RuntimeStatus { get; set; }
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
        public double DelaySeconds { get; set; } = 5;
        public double MaxDelaySeconds { get; set; } = 60;
        public double StartDelaySeconds { get; set; }
        public double MaxRetries { get; set; } = 10;
        public double BackoffCoefficient { get; set; } = 1.2;
    }
}
