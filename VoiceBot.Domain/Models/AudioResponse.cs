namespace VoiceBot.Domain.Models;

public class AudioResponse
{
    public byte[] AudioData { get; set; }
    public string ContentType { get; set; } = "audio/wav";
}
