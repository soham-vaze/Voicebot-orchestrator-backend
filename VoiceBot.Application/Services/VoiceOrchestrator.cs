using VoiceBot.Domain.Models;

namespace VoiceBot.Application.Services;

public class VoiceOrchestrator : IVoiceOrchestrator
{
    public async Task<(string text, byte[] audio)> ProcessAudioAsync(AudioRequest request)
    {
        // TODO: Replace with STT → LLM → TTS later

        string dummyText = "Hello! This is a dummy response from backend.";

        // For now, return empty audio
        byte[] dummyAudio = new byte[0];

        return (dummyText, dummyAudio);
    }
}
