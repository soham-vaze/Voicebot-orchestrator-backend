using VoiceBot.Domain.Models;

namespace VoiceBot.Application.Services;

public interface IVoiceOrchestrator
{
    Task<(string text, byte[] audio)> ProcessAudioAsync(AudioRequest request);
}
