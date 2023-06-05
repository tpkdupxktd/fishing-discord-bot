using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json;

public class Fish
{
    public string Name { get; set; }
    public int Rarity { get; set; }
    public int Price { get; set; }
}

public class User
{
    public ulong Id { get; set; }
    public int Balance { get; set; }
    public List<Fish> FishInventory { get; set; }
}

public class DailyReward
{
    public ulong UserId { get; set; }
    public DateTime LastClaimed { get; set; }
}

public class FishingGameBot
{
    private DiscordSocketClient _client;
    private CommandService _commands;
    private IServiceProvider _services;
    private Dictionary<ulong, User> _users;
    private List<Fish> _fishList;
    private List<DailyReward> _dailyRewards;
    private string _usersFile = "users.json";
    private string _fishFile = "fish.json";
    private string _dailyRewardsFile = "daily_rewards.json";

    public async Task MainAsync()
    {
        _client = new DiscordSocketClient();
        _commands = new CommandService();
        _client.Log += Log;

        await Initialize();
        await RegisterCommands();

        await _client.LoginAsync(TokenType.Bot, "YOUR_DISCORD_BOT_TOKEN");
        await _client.StartAsync();

        await Task.Delay(-1);
    }

    private Task Log(LogMessage arg)
    {
        Console.WriteLine(arg);
        return Task.CompletedTask;
    }

    private async Task Initialize()
    {
        _client.Ready += LoadData;
        _client.UserJoined += CreateUser;

        _client.MessageReceived += HandleCommandAsync;

        _users = await LoadUserData();
        _fishList = await LoadFishData();
        _dailyRewards = await LoadDailyRewardsData();
    }

    private async Task LoadData()
    {
        _users = await LoadUserData();
        _fishList = await LoadFishData();
        _dailyRewards = await LoadDailyRewardsData();
    }

    private async Task RegisterCommands()
    {
        _client.MessageReceived += HandleCommandAsync;
        await _commands.AddModulesAsync(typeof(FishingGameBot).Assembly, _services);
    }

    private async Task HandleCommandAsync(SocketMessage arg)
    {
        var message = arg as SocketUserMessage;
        var context = new SocketCommandContext(_client, message);

        if (message.Author.IsBot) return;

        int argPos = 0;
        if (message.HasStringPrefix("!", ref argPos))
        {
            var result = await _commands.ExecuteAsync(context, argPos, _services);
            if (!result.IsSuccess)
                Console.WriteLine(result.ErrorReason);
        }
    }

    private async Task CreateUser(SocketGuildUser user)
    {
        if (!_users.ContainsKey(user.Id))
        {
            var newUser = new User
            {
                Id = user.Id,
                Balance = 0,
                FishInventory = new List<Fish>()
            };

            _users.Add(user.Id, newUser);
            await SaveUserData();
        }
    }

    private async Task<List<Fish>> LoadFishData()
    {
        if (File.Exists(_fishFile))
        {
            var json = await File.ReadAllTextAsync(_fishFile);
            return JsonConvert.DeserializeObject<List<Fish>>(json);
        }
        else
        {
            var fishData = new List<Fish>
            {
                new Fish { Name = "Fish 1", Rarity = 1, Price = 10 },
                new Fish { Name = "Fish 2", Rarity = 1, Price = 10 },
                // Добавьте другие виды рыб
            };

            var json = JsonConvert.SerializeObject(fishData);
            await File.WriteAllTextAsync(_fishFile, json);

            return fishData;
        }
    }

    private async Task SaveFishData()
    {
        var json = JsonConvert.SerializeObject(_fishList);
        await File.WriteAllTextAsync(_fishFile, json);
    }

    private async Task<Dictionary<ulong, User>> LoadUserData()
    {
        if (File.Exists(_usersFile))
        {
            var json = await File.ReadAllTextAsync(_usersFile);
            return JsonConvert.DeserializeObject<Dictionary<ulong, User>>(json);
        }
        else
        {
            var userData = new Dictionary<ulong, User>();

            var json = JsonConvert.SerializeObject(userData);
            await File.WriteAllTextAsync(_usersFile, json);

            return userData;
        }
    }

    private async Task SaveUserData()
    {
        var json = JsonConvert.SerializeObject(_users);
        await File.WriteAllTextAsync(_usersFile, json);
    }

    private async Task<List<DailyReward>> LoadDailyRewardsData()
    {
        if (File.Exists(_dailyRewardsFile))
        {
            var json = await File.ReadAllTextAsync(_dailyRewardsFile);
            return JsonConvert.DeserializeObject<List<DailyReward>>(json);
        }
        else
        {
            var dailyRewardsData = new List<DailyReward>();

            var json = JsonConvert.SerializeObject(dailyRewardsData);
            await File.WriteAllTextAsync(_dailyRewardsFile, json);

            return dailyRewardsData;
        }
    }

    private async Task SaveDailyRewardsData()
    {
        var json = JsonConvert.SerializeObject(_dailyRewards);
        await File.WriteAllTextAsync(_dailyRewardsFile, json);
    }

    [Command("balance")]
    public async Task GetBalance(SocketCommandContext context)
    {
        var user = GetUser(context.User.Id);
        await context.Channel.SendMessageAsync($"Your balance is {user.Balance}");
    }

    [Command("daily")]
    public async Task ClaimDailyReward(SocketCommandContext context)
    {
        var userId = context.User.Id;
        var lastClaimed = _dailyRewards.FirstOrDefault(x => x.UserId == userId)?.LastClaimed ?? DateTime.MinValue;
        var timeSinceLastClaimed = DateTime.Now - lastClaimed;

        if (timeSinceLastClaimed.TotalHours < 12)
        {
            var remainingTime = TimeSpan.FromHours(12) - timeSinceLastClaimed;
            await context.Channel.SendMessageAsync($"You can claim your daily reward again in {remainingTime.Hours} hours and {remainingTime.Minutes} minutes.");
            return;
        }

        var user = GetUser(userId);
        user.Balance += 100; // Награда в 100 монет
        await context.Channel.SendMessageAsync("You claimed your daily reward of 100 coins!");

        var dailyReward = _dailyRewards.FirstOrDefault(x => x.UserId == userId);
        if (dailyReward != null)
            dailyReward.LastClaimed = DateTime.Now;
        else
            _dailyRewards.Add(new DailyReward { UserId = userId, LastClaimed = DateTime.Now });

        await SaveUserData();
        await SaveDailyRewardsData();
    }

    [Command("buy")]
    public async Task BuyItem(SocketCommandContext context, string itemName)
    {
        var user = GetUser(context.User.Id);
        var item = _fishList.FirstOrDefault(x => x.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase));

        if (item == null)
        {
            await context.Channel.SendMessageAsync("Item not found.");
            return;
        }

        if (user.Balance < item.Price)
        {
            await context.Channel.SendMessageAsync("Insufficient balance.");
            return;
        }

        user.Balance -= item.Price;
        user.FishInventory.Add(item);
        await context.Channel.SendMessageAsync($"You purchased {item.Name} for {item.Price} coins!");

        await SaveUserData();
    }

    [Command("sell")]
    public async Task SellItem(SocketCommandContext context, string itemName)
    {
        var user = GetUser(context.User.Id);
        var item = user.FishInventory.FirstOrDefault(x => x.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase));

        if (item == null)
        {
            await context.Channel.SendMessageAsync("Item not found in your inventory.");
            return;
        }

        user.Balance += item.Price;
        user.FishInventory.Remove(item);
        await context.Channel.SendMessageAsync($"You sold {item.Name} for {item.Price} coins!");

        await SaveUserData();
    }

    private User GetUser(ulong userId)
    {
        if (_users.TryGetValue(userId, out var user))
            return user;

        var newUser = new User
        {
            Id = userId,
            Balance = 0,
            FishInventory = new List<Fish>()
        };

        _users.Add(userId, newUser);
        return newUser;
    }
}

class Program
{
    static void Main(string[] args)
    {
        var bot = new FishingGameBot();
        bot.MainAsync().GetAwaiter().GetResult();
    }
}
