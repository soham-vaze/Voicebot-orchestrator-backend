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
    public string SessionID { get; set; }
}
