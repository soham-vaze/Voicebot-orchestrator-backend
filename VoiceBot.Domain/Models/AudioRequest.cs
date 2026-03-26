namespace VoiceBot.Domain.Models;

// public class AudioRequest
// {
//     public byte[] AudioData { get; set; }
//     public string FileName { get; set; }
// }


public class AudioRequest
{
    public byte[] AudioData { get; set; }
    public string FileName { get; set; }

    /// <summary>
    /// Caller-supplied session identifier for multi-turn conversation continuity.
    /// When null or empty the orchestrator generates a new session ID automatically.
    /// </summary>
    public string? SessionId { get; set; }
}
