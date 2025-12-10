using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

enum ConversationState
{
    None,
    WaitingForMobile
}

class Program
{
    // TODO: put your real bot token here
    private static readonly string BotToken = Environment.GetEnvironmentVariable("8177040078:AAHaetgzv3Lf3UWBwBGtyiWdNHMm_cKc8Dw");

    // Base API endpoint (without term)
    private const string UserApiBaseUrl =
        "https://mynkapi.amit1100941.workers.dev/api?key=mynk01&type=mobile&term=";

    // per-chat state
    private static readonly ConcurrentDictionary<long, ConversationState> ChatStates =
        new ConcurrentDictionary<long, ConversationState>();

    static async Task Main()
    {
        var botClient = new TelegramBotClient(BotToken);

        using var cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message }
        };

        botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            cancellationToken: cts.Token
        );

        var me = await botClient.GetMeAsync(cts.Token);
        Console.WriteLine($"Bot @{me.Username} is running. Press Enter to exit...");
        Console.ReadLine();

        cts.Cancel();
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Type != UpdateType.Message || update.Message!.Type != MessageType.Text)
            return;

        var message = update.Message;
        var chatId = message.Chat.Id;
        var text = message.Text!.Trim();

        // default state is None
        var state = ChatStates.GetOrAdd(chatId, ConversationState.None);

        if (text.Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            ChatStates[chatId] = ConversationState.None;
            await SendMainMenu(bot, chatId, ct);
            return;
        }

        // Menu option
        if (text.Equals("Search user by mobile", StringComparison.OrdinalIgnoreCase))
        {
            ChatStates[chatId] = ConversationState.WaitingForMobile;
            await bot.SendTextMessageAsync(
                chatId,
                "Please Enter Mobile number:",
                cancellationToken: ct
            );
            return;
        }

        // If we are waiting for mobile number
        if (state == ConversationState.WaitingForMobile)
        {
            var mobile = text;

            // simple validation
            if (mobile.Length < 5 || mobile.Length > 15)
            {
                await bot.SendTextMessageAsync(
                    chatId,
                    "That doesn't look like a valid mobile number. Try again.",
                    cancellationToken: ct
                );
                return;
            }

            await bot.SendTextMessageAsync(chatId, "Searching, please wait...", cancellationToken: ct);

            var resultText = await SearchUserByMobileAsync(mobile);

            await bot.SendTextMessageAsync(
                chatId,
                resultText,
                parseMode: ParseMode.Markdown,
                cancellationToken: ct
            );

            // go back to main menu
            ChatStates[chatId] = ConversationState.None;
            await SendMainMenu(bot, chatId, ct);
            return;
        }

        // Unknown text in idle state → show menu
        await bot.SendTextMessageAsync(
            chatId,
            "I didn't understand that. Use the menu below.",
            cancellationToken: ct
        );
        await SendMainMenu(bot, chatId, ct);
    }

    private static async Task<string> SearchUserByMobileAsync(string mobile)
    {
        try
        {
            using var http = new HttpClient();

            var url = UserApiBaseUrl + Uri.EscapeDataString(mobile);
            var json = await http.GetStringAsync(url);

            var obj = JObject.Parse(json);

            bool success = obj["success"]?.Value<bool>() ?? false;
            if (!success)
            {
                return "❌ User not found.";
            }

            // FIX: the API returns "result": [ { ... } ]
            var resultArray = obj["result"] as JArray;
            if (resultArray == null || resultArray.Count == 0)
            {
                return "❌ No user found in API response.";
            }

            var data = resultArray[0]; // first result

            // Extract values
            string id = data["id"]?.ToString() ?? "N/A";
            string _mobile = data["mobile"]?.ToString() ?? "N/A";
            string name = data["name"]?.ToString() ?? "N/A";
            string father_name = data["father_name"]?.ToString() ?? "N/A";
            string address = data["address"]?.ToString() ?? "N/A";
            string alt_mobile = data["alt_mobile"]?.ToString() ?? "N/A";
            string circle = data["circle"]?.ToString() ?? "N/A";
            string id_number = data["id_number"]?.ToString() ?? "N/A";
            string email = data["email"]?.ToString() ?? "N/A";

            return
                $"✅ *User found Successfully:*\n" +
                $"*Name:* {name}\n" +
                $"*Mobile:* {_mobile}\n" +
                $"*Father's Name:* {father_name}\n" +
                $"*Address:* {address}\n" +
                $"*Alternative Mob.No:* {alt_mobile}\n" +
                $"*SIM Provider:* {circle}\n" +
                $"*ID:* {id_number}\n" +
                $"*Email:* {email}";
        }
        catch (Exception ex)
        {
            return "⚠️ Error calling API: " + ex.Message;
        }
    }


    private static async Task SendMainMenu(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Search user by mobile" }
        })
        {
            ResizeKeyboard = true
        };

        await bot.SendTextMessageAsync(
            chatId,
            "What do you want to do?",
            replyMarkup: keyboard,
            cancellationToken: ct
        );
    }

    private static Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        var errorMessage = ex switch
        {
            ApiRequestException apiEx =>
                $"Telegram API Error:\n[{apiEx.ErrorCode}] {apiEx.Message}",
            _ => ex.ToString()
        };

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(errorMessage);
        Console.ResetColor();

        return Task.CompletedTask;
    }
}
