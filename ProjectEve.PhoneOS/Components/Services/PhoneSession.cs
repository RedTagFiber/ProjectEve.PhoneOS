namespace ProjectEve.PhoneOS.Services;

public class PhoneSession
{
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public List<ChatMessage> Messages { get; } = new()
    {
        new ChatMessage("hey. i’m here", false, DateTime.Now),
        new ChatMessage("testing the phone", true, DateTime.Now),
        new ChatMessage("looks good so far", false, DateTime.Now)
    };

    public record ChatMessage(string Text, bool IsMine, DateTime At);
}