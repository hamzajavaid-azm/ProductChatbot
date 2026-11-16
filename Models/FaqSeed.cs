namespace ProductChatbot.Models
{
    // Shape of an entry in Data/faq.json, used only for optional one-time startup seeding.
    public record FaqSeed(string Question, string Answer);
}
