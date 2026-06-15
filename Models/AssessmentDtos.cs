namespace SnapToolCloud.Models
{
    public static class AssessmentStatuses
    {
        public const string NoRestrictions = "NoRestrictions";
        public const string Monitor = "Monitor";
        public const string DoNotProceed = "DoNotProceed";
    }

    public sealed class ManualVesselInfo
    {
        public string? Berth { get; set; }
        public string? Vessel { get; set; }
        public string? MC { get; set; }
        public string? VesselName { get; set; }
    }

    public sealed class WindAssessmentRequest
    {
        public DateTime AssessmentDateTime { get; set; }
        public ManualVesselInfo? Vessel { get; set; }
    }

    public sealed class WaveAssessmentRequest
    {
        public DateTime AssessmentDateTime { get; set; }
        public ManualVesselInfo? Vessel { get; set; }
        public bool VesselMovementObserved { get; set; }
    }

    public sealed class PassingVesselAssessmentRequest
    {
        public List<PassingVesselMovement> PassingVessels { get; set; } = new();
    }

    public sealed class PassingVesselMovement
    {
        public string? VesselName { get; set; }
        public string? Location { get; set; }
        public string? MovementType { get; set; }
        public DateTime? ScheduledDateTime { get; set; }
    }

    public sealed class AssessmentResponse
    {
        public string Status { get; set; } = AssessmentStatuses.NoRestrictions;
        public List<string> RecommendedActions { get; set; } = new();
        public List<string> TriggeredRules { get; set; } = new();
        public Dictionary<string, object?> SupportingFacts { get; set; } = new();
    }

    public sealed class ApiResponse<T>
    {
        public string Status { get; set; } = "success";
        public T? Result { get; set; }
        public string? Message { get; set; }
    }
}
