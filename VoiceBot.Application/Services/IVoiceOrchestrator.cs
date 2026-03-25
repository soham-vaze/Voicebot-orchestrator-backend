using VoiceBot.Domain.Models;

namespace VoiceBot.Application.Services;

public interface IVoiceOrchestrator
{
    Task<(string query, string text, byte[] audio)> ProcessAudioAsync(AudioRequest request);
}
