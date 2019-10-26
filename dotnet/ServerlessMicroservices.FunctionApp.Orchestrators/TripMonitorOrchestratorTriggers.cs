using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace ServerlessMicroservices.FunctionApp.Orchestrators
{
    public static class TripMonitorOrchestratorTriggers
    {
        [FunctionName("T_StartTripMonitorViaQueueTrigger")]
        public static async Task StartTripMonitorViaQueueTrigger(
            [OrchestrationClient] DurableOrchestrationClient context,
            [QueueTrigger("%TripMonitorsQueue%", Connection = "AzureWebJobsStorage")] string code,
            ILogger log)
        {
            try
            {
                // The monitor instance id is the trip code + -M. This is to make sure that a Trip Manager and a monitor can co-exist
                var instanceId = $"{code}-M";
                await StartInstance(context, code, instanceId, log);
            }
            catch (Exception ex)
            {
                var error = $"StartTripMonitorViaQueueTrigger failed: {ex.Message}";
                log.LogError(error);
            }
        }
        //**Durable Function - 3 "Trip Monitor Orchestrator Trigger"**  
        //Rideshare uses a Sequencial Durable Function (waits the result of previous activities before calling the next)
        //Once orchestrator has handed off to activity function it is able to scale to 0 (hence not long-running)
        //Orchestrator and Activity functions write progress to Execution history (in this case SQL DB)
        //When the Orchestrator wakes back up again, it starts here, but then checks execution history
        [FunctionName("T_StartTripMonitor")]
        public static async Task<IActionResult> StartTripMonitor([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "tripmonitors/{code}")] HttpRequest req,
            [OrchestrationClient] DurableOrchestrationClient context,
            string code,
            ILogger log)
        {
            try
            {
                // The monitor instance id is the trip code + -M. This is to make sure that a Trip Manager and a monitor can co-exist
                var instanceId = $"{code}-M";
                await StartInstance(context, code, instanceId, log);

                //NOTE: Unfortunately this does not work the same way as before when it was using HttpMessageResponse
                //var reqMessage = req.ToHttpRequestMessage();
                //var res = context.CreateCheckStatusResponse(reqMessage, trip.Code);
                //res.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
                //return (ActionResult)new OkObjectResult(res.Content);
                return (ActionResult)new OkObjectResult("NOTE: No status URLs are returned!");
            }
            catch (Exception ex)
            {
                var error = $"StartTripMonitor failed: {ex.Message}";
                log.LogError(error);
                return new BadRequestObjectResult(error);
            }
        }

        [FunctionName("T_GetTripMonitor")]
        public static async Task<IActionResult> GetTripMonitor([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tripmonitors/{code}")] HttpRequest req,
            [OrchestrationClient] DurableOrchestrationClient context,
            string code,
            ILogger log)
        {
            try
            {
                var status = await context.GetStatusAsync(code);
                if (status == null)
                    throw new Exception($"{code} does not exist!!");

                return (ActionResult)new OkObjectResult(status);
            }
            catch (Exception ex)
            {
                var error = $"GetTripMonitor failed: {ex.Message}";
                log.LogError(error);
                return new BadRequestObjectResult(error);
            }
        }

        [FunctionName("T_TerminateTripMonitor")]
        public static async Task<IActionResult> TerminateTripMonitor([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "tripmonitors/{code}/terminate")] HttpRequest req,
            [OrchestrationClient] DurableOrchestrationClient context,
            string code,
            ILogger log)
        {
            try
            {
                await TeminateInstance(context, code, log);
                return (ActionResult)new OkObjectResult("Ok");
            }
            catch (Exception ex)
            {
                var error = $"TerminateTripMonitor failed: {ex.Message}";
                log.LogError(error);
                return new BadRequestObjectResult(error);
            }
        }

        //TODO: Implement Get Trip Monitor Instances, Restart Trip Monitor Instances and Terminate Trip Monitor Instances if Persist to table storage if persist instances is activated

        /** PRIVATE **/
        private static async Task StartInstance(DurableOrchestrationClient context, string code, string instanceId, ILogger log)
        {
            try
            {
                var reportStatus = await context.GetStatusAsync(instanceId);
                string runningStatus = reportStatus == null ? "NULL" : reportStatus.RuntimeStatus.ToString();
                log.LogInformation($"Instance running status: '{runningStatus}'.");

                if (reportStatus == null || reportStatus.RuntimeStatus != OrchestrationRuntimeStatus.Running)
                {
                    await context.StartNewAsync("O_MonitorTrip", instanceId, code);
                    log.LogInformation($"Started a new trip monitor = '{instanceId}'.");

                    // TODO: Persist to table storage if persist instances is activated
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private static async Task TeminateInstance(DurableOrchestrationClient context, string instanceId, ILogger log)
        {
            try
            {
                // TODO: Remove from table storage if persist instances is activated
                log.LogInformation($"Terminating trip monitor '{instanceId}'.");
                await context.TerminateAsync(instanceId, "Via an API request");
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
