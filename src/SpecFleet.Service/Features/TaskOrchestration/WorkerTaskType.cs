namespace SpecFleet.Service.Features.TaskOrchestration;

public enum WorkerTaskType
{
    GenerateWorker,
    GenerateUiPage,
    GenerateTerraformModule,
    GenerateCloudflareTunnelConfig,
    GenerateRabbitMqConfig,
    GenerateControlPlaneLogic
}
