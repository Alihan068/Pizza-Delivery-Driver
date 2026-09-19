/// <summary>One structural problem found by <see cref="TrafficMapValidator"/>, with a stable machine-readable code and the offending id.</summary>
[System.Serializable]
public class TrafficValidationIssue {
    public string code;
    public string referenceId;
    public string message;

    public TrafficValidationIssue(string code, string referenceId, string message) {
        this.code = code;
        this.referenceId = referenceId;
        this.message = message;
    }
}
