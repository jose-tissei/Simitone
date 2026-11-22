using System.Collections.Generic;
using Simitone.Conversations;

// The words used to ask a language model for lines. Kept apart from the generator so the exact wording is easy to review
// and change, and so tests can check that every interaction and tone has a description.
public static class Prompts
{
    // what the first Sim is trying to do, as a verb phrase
    public static readonly Dictionary<string, string> Action = new Dictionary<string, string>
    {
        ["apologize"] = "apologize for something they did",
        ["backrub"] = "offer a back rub",
        ["brag"] = "brag about something they are proud of",
        ["cheerup"] = "cheer them up because they seem sad",
        ["compliment"] = "give them a compliment",
        ["dancewith"] = "ask them to dance",
        ["entertain"] = "entertain them with a funny act",
        ["flirt"] = "flirt with them",
        ["givegift"] = "give them a gift",
        ["hug"] = "give them a hug",
        ["insult"] = "tease them with a mild insult",
        ["joke"] = "tell them a joke",
        ["kiss"] = "kiss them",
        ["propose"] = "propose marriage to them",
        ["scare"] = "playfully scare them",
        ["slap"] = "give them a playful slap",
        ["tease"] = "tease them",
        ["tickle"] = "tickle them",
    };

    // the other Sim's side of hostile interactions, used for the neutral replies
    public static readonly Dictionary<string, string> Received = new Dictionary<string, string>
    {
        ["insult"] = "being teased with a mild insult",
        ["scare"] = "being playfully scared",
        ["slap"] = "being playfully slapped",
        ["tease"] = "being teased",
        ["propose"] = "a marriage proposal",
    };

    public static readonly string[] ToneDescription =
    {
        "gentle, warm and caring",
        "playful, joking and light-hearted",
        "bold, direct and confident",
    };

    public static string Person(Temperament t)
    {
        switch (t)
        {
            case Simitone.Conversations.Temperament.EasilyOffended: return "sensitive and easily offended";
            case Simitone.Conversations.Temperament.ThickSkinned: return "thick-skinned and enjoys banter";
            case Simitone.Conversations.Temperament.Cold: return "cold, distant and hard to impress";
            case Simitone.Conversations.Temperament.Receptive: return "warm and open to people";
            default: return "an ordinary person";
        }
    }

    public static string Result(int quality)
    {
        return quality == 1 ? "bad: they are put off, embarrassed or say no"
             : quality == 3 ? "great: they are delighted and respond warmly"
             : "okay: they respond politely but without much enthusiasm";
    }

    public const string Rules =
        "The game is rated PG. Write only spoken dialogue: no stage directions, no emojis, no quotation marks, no line breaks. " +
        "Each line must be under 90 characters, plain English, and sound like a different person each time. ";

    public static string Speaker(string key, int tone, int count)
    {
        return "You write dialogue for a Sims-style life simulation game. A Sim wants to " + Action[key] + ". " +
               "Write " + count + " different short things this Sim says out loud to the other Sim, in a " + ToneDescription[tone] + " tone. " +
               "Use the placeholder {name} where the other Sim's name goes in at least half of the lines. " + Rules +
               "Reply only as JSON: {\"lines\": [\"...\", \"...\"]}";
    }

    public static string Reply(string key, Temperament t, int quality, int count)
    {
        var what = Received.ContainsKey(key) ? Received[key] : "the Sim's attempt to " + Action[key];
        return "You write dialogue for a Sims-style life simulation game. A Sim called {me} just tried this on another Sim: " + Action[key] + ". " +
               "The other Sim is " + Person(t) + ". The reaction is " + Result(quality) + ". " +
               "Write " + count + " different short things the other Sim says back out loud. " +
               "You may use the placeholder {me} for the first Sim's name in some lines. " + Rules +
               "Reply only as JSON: {\"lines\": [\"...\", \"...\"]}";
    }
}

// Talk is not one of the 18 outcome interactions: the speaker chooses a subject (one of the eight interests) and the
// other Sim reacts to how much they like it. Lines use {topic} for the subject's name.
public static class TalkPrompts
{
    public static string Speaker(int tone, int count)
    {
        return "You write dialogue for a Sims-style life simulation game. A Sim starts a conversation with another Sim about a subject, in a " +
               Prompts.ToneDescription[tone] + " tone. Write " + count + " different short things this Sim says out loud to bring the subject up. " +
               "Use the placeholder {topic} for the subject (for example: Have you heard anything about {topic}?) in every line, and {name} for the other Sim's name in some. " +
               Prompts.Rules + "Reply only as JSON: {\"lines\": [\"...\", \"...\"]}";
    }

    public static string Reply(Temperament t, int quality, int count)
    {
        var feeling = quality == 1 ? "they are bored by the subject {topic} and show it"
                    : quality == 3 ? "they love the subject {topic} and get excited"
                    : "they have mild interest in the subject {topic}";
        return "You write dialogue for a Sims-style life simulation game. A Sim called {me} just started talking to another Sim about {topic}. " +
               "The other Sim is " + Prompts.Person(t) + "; " + feeling + ". Write " + count + " different short things the other Sim says back out loud. " +
               "You may use {topic} and {me} as placeholders. " + Prompts.Rules + "Reply only as JSON: {\"lines\": [\"...\", \"...\"]}";
    }
}
