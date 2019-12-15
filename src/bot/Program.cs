using System;
using System.Collections.Generic;
using Telegram.Bot;
using Telegram.Bot.Args;
using MySql.Data.MySqlClient;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types.Enums;
using System.Linq;
using System.Text;
using System.Data;
using Nancy.Hosting.Self;
using Nancy;
using System.Threading;
using DigitalOcean.API;
using DigitalOcean.API.Models.Requests;
using xHTTP;
using System.ComponentModel;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace TGOpen
{
    public sealed class MainModule : NancyModule
    {
        public class NancyBotResponse
        {
            public string Status { get; set; }
            public string Message { get; set; }
        }

        public MainModule()
        {
            try
            {
                Get("/", args => new Response { StatusCode = HttpStatusCode.NotFound });
                Get("/api/ping", args => Response.AsJson(new NancyBotResponse { Status = "Pong" }));

                Get("/api/stop", args =>
                {
                    Program.Are.Set();
                    return Response.AsJson(new NancyBotResponse { Status = "Success", Message = "[[TGOpen]] (" + DateTime.Now.ToLongTimeString() + ") Бот останавливается!" });
                });

                Post("/api/auth_complete", async args =>
                {
                    if (Request.Form["token"] != null)
                    {
                        var token = (string)Request.Form["token"];
                        using (var dbOpen = new Program.Db(Program.DbConnStr))
                        {
                            var data = dbOpen.Query($"SELECT id_user FROM DATA WHERE token = '{token}'");
                            if (data.Rows.Count <= 0)
                                return Response.AsJson(new NancyBotResponse
                                {
                                    Status = "Error",
                                    Message = "[[TGOpen]] (" + DateTime.Now.ToLongTimeString() +
                                              ") Пользователь не найден!"
                                });
                            var row = data.Rows[0];
                            var userChatId = long.Parse(row["id_user"].ToString());
                            const string message = "Аккаунт DigitalOcean успешно подключен. Вы можете приступить к созданию дроплета.";

                            var keyboard = new InlineKeyboardMarkup(
                                new[]
                                {
                                    new[]
                                    {
                                        InlineKeyboardButton.WithCallbackData("Создать новый дроплет", "create_droplet")
                                    }
                                }
                            );
                            await Program.Bot.SendTextMessageAsync(userChatId, message, ParseMode.Markdown, false, false, 0, keyboard);
                            return Response.AsJson(new NancyBotResponse { Status = "Success", Message = "[[TGOpen]] (" + DateTime.Now.ToLongTimeString() + ") Сообщение отправлено!" });
                        }
                    }
                    else
                    {
                        return Response.AsJson(new NancyBotResponse { Status = "Error", Message = "[[TGOpen]] (" + DateTime.Now.ToLongTimeString() + ") Получены не все данные!" });
                    }
                });
            }
            catch
            {
                // ignored
            }
        }
    }

    internal class Program
    {
        public static TelegramBotClient Bot;
        public static string DbConnStr = "";
        public static string DoAuthUrl = "";
        public static List<string> ReferalLinks = new List<string>();
        public static List<string> DoRegions = new List<string>();

        public static AutoResetEvent Are = new AutoResetEvent(false);

        public class Db : IDisposable
        {
            private readonly MySqlConnection _conn;
            public Db(string dbConnStr)
            {
                _conn = new MySqlConnection(dbConnStr);
                _conn.Open();
            }

            public DataTable Query(string sql)
            {
                if (_conn.State != ConnectionState.Open)
                {
                    _conn.Open();
                }
                var dt = new DataTable();
                using (var command = new MySqlCommand(sql, _conn))
                {
                    var reader = command.ExecuteReader();
                    if (reader.HasRows)
                    {
                        dt.Load(reader);
                    }
                    return dt;
                }
            }

            public int NoneQuery(string sql)
            {
                if (_conn.State != ConnectionState.Open)
                {
                    _conn.Open();
                }
                using (var command = new MySqlCommand(sql, _conn))
                {
                    var result = command.ExecuteNonQuery();
                    return result;
                }
            }

            public int Count(string sql)
            {
                if (_conn.State != ConnectionState.Open)
                {
                    _conn.Open();
                }
                using (var cmd = new MySqlCommand(sql, _conn))
                {
                    var count = Convert.ToInt32(cmd.ExecuteScalar());
                    return count;
                }
            }

            public string GenerateUniqueId()
            {
                var builder = new StringBuilder();
                Enumerable
                    .Range(65, 26)
                    .Select(e => ((char)e).ToString())
                    .Concat(Enumerable.Range(97, 26).Select(e => ((char)e).ToString()))
                    .Concat(Enumerable.Range(0, 10).Select(e => e.ToString()))
                    .OrderBy(e => Guid.NewGuid())
                    .Take(32)
                    .ToList().ForEach(e => builder.Append(e));
                return builder.ToString();
            }

            public void Dispose()
            {
                _conn.Close();
            }
        }

        public class Server
        {
            public string Ip { get; set; }
            public int Port { get; set; }
            public string Secret { get; set; }
        }

        public static string Declension(int number, string nominative, string genitiveSingular, string genitivePlural)
        {
            var lastDigit = number % 10;
            var lastTwoDigits = number % 100;
            switch (lastDigit)
            {
                case 1 when lastTwoDigits != 11:
                    return nominative;
                case 2 when lastTwoDigits != 12:
                case 3 when lastTwoDigits != 13:
                case 4 when lastTwoDigits != 14:
                    return genitiveSingular;
                default:
                    return genitivePlural;
            }
        }

        private static void Main()
        {
            HttpRequest.TimeOutGlobal = 1000 * 300; // 5 min.

            ReferalLinks.Add("");
            ReferalLinks.Add("");
            DoRegions.Add("ams3");
            DoRegions.Add("fra1");

            Bot = new TelegramBotClient("");
            Bot.OnCallbackQuery += BotOnQueryReceived;
            Bot.OnMessage += BotOnMessage;
            var me = Bot.GetMeAsync().Result;
            Console.Title = me.Username;
            Bot.StartReceiving();

            var configuration = new HostConfiguration
            {
                UrlReservations = new UrlReservations { CreateAutomatically = true }
            };

            using (var host = new NancyHost(configuration, new Uri("http://localhost:7777")))
            {
                host.Start();
                Console.WriteLine("Сервер запущен!");
                Are.WaitOne();
                Thread.Sleep(TimeSpan.FromSeconds(1));
                Console.WriteLine("Сервер остановлен!");
            }
        }
        public static InlineKeyboardMarkup InlineKeyboardMarkupMaker(Dictionary<string, string> items, int columns)
        {
            var rows = (int)Math.Ceiling(items.Count / (double)columns);
            var buttons = new InlineKeyboardButton[rows][];
            for (var i = 0; i < buttons.Length; i++)
            {
                buttons[i] = items.Skip(i * columns)
                    .Take(columns).Select(item =>
                    InlineKeyboardButton.WithCallbackData(item.Key, item.Value)
                    ).ToArray();
            }
            return new InlineKeyboardMarkup(buttons);
        }
        public static string[] Explode(string separator, string input)
        {
            try
            {
                return input.Split(new[] { separator }, StringSplitOptions.None);
            }
            catch
            {
                return new string[0];
            }
        }

        private static async void BotOnMessage(object sender, MessageEventArgs e)
        {
            switch (e.Message.Text)
            {
                case "/start":
                    //await Bot.DeleteMessageAsync(e.Message.Chat.Id, e.Message.MessageId);
                    await Bot.SendTextMessageAsync(e.Message.Chat.Id, "⚠️ При выкладывании прокси-сервера в публичный доступ высокий процент его блокировки, имейте это ввиду! ⚠️");
                    using (var dbOpen = new Db(DbConnStr))
                    {
                        var userChatId = e.Message.Chat.Id;
                        var userToken = dbOpen.GenerateUniqueId();

                        var inBase = dbOpen.Count($"SELECT COUNT(*) FROM DATA WHERE id_user = {userChatId}") > 0;
                        if (!inBase)
                        {
                            dbOpen.NoneQuery($"INSERT INTO DATA(id_user, token) VALUES('{userChatId}', '{userToken}')");
                        }
                        else
                        {
                            var data = dbOpen.Query($"SELECT token FROM DATA WHERE id_user = {userChatId}");
                            if (data.Rows.Count > 0)
                            {
                                var row = data.Rows[0];
                                userToken = row["token"].ToString();
                            }
                        }

                        var doData = dbOpen.Query($"SELECT ip, hash, do_access, do_droplet FROM DATA WHERE id_user = {userChatId}");
                        if (doData.Rows.Count > 0)
                        {
                            var row = doData.Rows[0];
                            var doToken = row["do_access"].ToString();
                            var doDroplet = row["do_droplet"].ToString();
                            var ip = row["ip"].ToString();
                            var hash = row["hash"].ToString();
                            if (string.IsNullOrEmpty(doToken))
                            {
                                goto Login;
                            }
                            try
                            {
                                var doClient = new DigitalOceanClient(doToken);
                                var account = await doClient.Account.Get();
                            }
                            catch (DigitalOcean.API.Exceptions.ApiException)
                            {
                                dbOpen.NoneQuery(
                                    $"UPDATE DATA SET do_access = '', do_refresh = '' WHERE id_user = {userChatId}");
                                await Bot.SendTextMessageAsync(e.Message.Chat.Id, "Ваша сессия DigitalOcean устарела. Требуется повторная привязка аккаунта!");
                                goto Login;
                            }

                            if (string.IsNullOrEmpty(doDroplet) || doDroplet == "0")
                            {
                                const string messageDo = "Вы можете приступить к созданию дроплета на базе DigitalOcean.";
                                var keyboardDo = new InlineKeyboardMarkup(
                                    new[]
                                    {
                                        new[]
                                        {
                                            InlineKeyboardButton.WithCallbackData("Создать новый дроплет", "create_droplet")
                                        }
                                    }
                                );
                                await Bot.SendTextMessageAsync(e.Message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                            }
                            else
                            {
                                try
                                {
                                    var doClient = new DigitalOceanClient(doToken);
                                    var droplet = await doClient.Droplets.Get(int.Parse(doDroplet));
                                    var status = droplet.Status;
                                }
                                catch (DigitalOcean.API.Exceptions.ApiException)
                                {
                                    dbOpen.NoneQuery($"UPDATE DATA SET do_droplet = '' WHERE id_user = {userChatId}");
                                    const string messageDo = "Дроплет недоступен или был удален. Рекомендуется создать новый.";
                                    var keyboardDo = new InlineKeyboardMarkup(
                                        new[]
                                        {
                                            new[]
                                            {
                                                InlineKeyboardButton.WithCallbackData("Создать новый дроплет", "create_droplet")
                                            }
                                        }
                                    );
                                    await Bot.SendTextMessageAsync(e.Message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                    return;
                                }

                                using (var request = new HttpRequest())
                                {
                                    var getServersPage = request.GET($"http://{ip}:1337/{hash}/getServers");
                                    if (getServersPage.IsOk)
                                    {
                                        var servers = JArray.Parse(getServersPage.Html);
                                        var userServers = JsonConvert.DeserializeObject<List<Server>>(servers.ToString());
                                        var possition = 0;
                                        var myServers = userServers.Aggregate("", (current, userServer) => current + string.Format("[[{4}]] [{0}:{1}](https://t.me/proxy?server={0}&port={1}&secret={2}){3}", userServer.Ip, userServer.Port, userServer.Secret, Environment.NewLine, ++possition));

                                        var messageDo =
                                            $"Все готово для развертки прокси-сервера! На данный момент у Вас {userServers.Count} {Declension(userServers.Count, "прокси-сервер", "прокси-сервера", "прокси-серверов")}.{Environment.NewLine + Environment.NewLine}{myServers}";
                                        if (userServers.Count == 0)
                                        {
                                            var keyboardDo = new InlineKeyboardMarkup(
                                                new[]
                                                {
                                                    new[]
                                                    {
                                                        InlineKeyboardButton.WithCallbackData("Создать прокси-сервер", "create_server")
                                                    }
                                                }
                                            );
                                            await Bot.SendTextMessageAsync(e.Message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                            return;
                                        }

                                        if (userServers.Count < 20)
                                        {
                                            var keyboardDo = new InlineKeyboardMarkup(
                                                new[]
                                                {
                                                    new[]
                                                    {
                                                        InlineKeyboardButton.WithCallbackData("Удалить прокси-сервер", "delete_servers")
                                                    },
                                                    new[]
                                                    {
                                                        InlineKeyboardButton.WithCallbackData("Создать прокси-сервер", "create_server")
                                                    }
                                                }
                                            );
                                            await Bot.SendTextMessageAsync(e.Message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                        }
                                        else
                                        {
                                            var keyboardDo = new InlineKeyboardMarkup(
                                                new[]
                                                {
                                                    new[]
                                                    {
                                                        InlineKeyboardButton.WithCallbackData("Удалить прокси-сервер", "delete_servers")
                                                    }
                                                }
                                            );
                                            await Bot.SendTextMessageAsync(e.Message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                        }
                                        return;
                                    }
                                    else
                                    {
                                        await Bot.SendTextMessageAsync(e.Message.Chat.Id, "Ошибка получения списка прокси-серверов.");
                                        return;
                                    }
                                }
                            }
                            return;
                        }
                        Login:
                        var message = "На данный момент поддерживается DigitalOcean." + Environment.NewLine +
                        "Для работы с ботом необходимо зарегистрироваться на сайте хостинг провайдера, чтобы поддержать создателей бота Вы можете это сделать по реферальной ссылке." + Environment.NewLine +
                        "Далее, необходимо пополнить баланс на 6 долларов (вкл. 20% НДС), чтобы была возможность создать сервер.";

                        var keyboard = new InlineKeyboardMarkup(
                            new[]
                            {
                                new[]
                                {
                                    InlineKeyboardButton.WithUrl("Регистрация (реф.)", ReferalLinks.PickRandom()),
                                    InlineKeyboardButton.WithUrl("Регистрация", "https://www.digitalocean.com/")
                                },
                                new[]
                                {
                                    InlineKeyboardButton.WithUrl("Подключить DigitalOcean", string.Format(DoAuthUrl, userToken))
                                }
                            }
                        );
                        await Bot.SendTextMessageAsync(e.Message.Chat.Id, message, ParseMode.Markdown, false, false, 0, keyboard);

                    }
                    break;

                default:
                    try
                    {
                        await Bot.SendTextMessageAsync(e.Message.Chat.Id, "Используйте команду /start для начала взаимодействия с ботом.");
                    }
                    catch
                    {
                        // ignored
                    }

                    break;
            }
        }

        private static async void BotOnQueryReceived(object sender, CallbackQueryEventArgs queryEventArgs)
        {
            try
            {
                var message = queryEventArgs.CallbackQuery.Message;
                var command = Explode(":", queryEventArgs.CallbackQuery.Data);
                var userChatId = message.Chat.Id;
                switch (command[0])
                {
                    case "delete_servers":
                        using (var dbOpen = new Db(DbConnStr))
                        {
                            var doData = dbOpen.Query($"SELECT ip, hash FROM DATA WHERE id_user = {userChatId}");
                            if (doData.Rows.Count > 0)
                            {
                                var row = doData.Rows[0];
                                var ip = row["ip"].ToString();
                                var hash = row["hash"].ToString();
                                await Bot.DeleteMessageAsync(message.Chat.Id, message.MessageId);
                                using (var request = new HttpRequest())
                                {
                                    var getServersPage = request.GET($"http://{ip}:1337/{hash}/getServers");
                                    if (getServersPage.IsOk)
                                    {
                                        var servers = JArray.Parse(getServersPage.Html);
                                        var userServers = JsonConvert.DeserializeObject<List<Server>>(servers.ToString());
                                        var myServers = userServers.ToDictionary(userServer => "Port:" + userServer.Port, userServer => "delete_proxy:" + userServer.Port);
                                        var messageDo =
                                            $"Выберите прокси-сервер который желаете удалить! На данный момент у Вас {userServers.Count} {Declension(userServers.Count, "прокси-сервер", "прокси-сервера", "прокси-серверов")}.";
                                        var keyboardDo = InlineKeyboardMarkupMaker(myServers.Take(20).ToDictionary(pair => pair.Key, pair => pair.Value), 4);
                                        await Bot.SendTextMessageAsync(message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                    }
                                    else
                                    {
                                        await Bot.SendTextMessageAsync(message.Chat.Id, "Ошибка получения списка прокси-серверов.");
                                    }
                                }
                            }
                        }
                        break;

                    case "delete_proxy":
                        using (var dbOpen = new Db(DbConnStr))
                        {
                            var doData = dbOpen.Query($"SELECT ip, hash FROM DATA WHERE id_user = {userChatId}");
                            if (doData.Rows.Count > 0)
                            {
                                var row = doData.Rows[0];
                                var ip = row["ip"].ToString();
                                var hash = row["hash"].ToString();
                                await Bot.DeleteMessageAsync(message.Chat.Id, message.MessageId);
                                using (var delRequest = new HttpRequest())
                                {
                                    var delServersPage = delRequest.GET(
                                        $"http://{ip}:1337/{hash}/deleteServer/{command[1]}");
                                    if (delServersPage.IsOk)
                                    {
                                        using (var request = new HttpRequest())
                                        {
                                            var getServersPage = request.GET($"http://{ip}:1337/{hash}/getServers");
                                            if (getServersPage.IsOk)
                                            {
                                                var servers = JArray.Parse(getServersPage.Html);
                                                var userServers = JsonConvert.DeserializeObject<List<Server>>(servers.ToString());
                                                var possition = 0;
                                                var myServers = userServers.Aggregate("", (current, userServer) => current + string.Format("[[{4}]] [{0}:{1}](https://t.me/proxy?server={0}&port={1}&secret={2}){3}", userServer.Ip, userServer.Port, userServer.Secret, Environment.NewLine, ++possition));
                                                var messageDo =
                                                    $"Прокси сервер удален! На данный момент у Вас {userServers.Count} {Declension(userServers.Count, "прокси-сервер", "прокси-сервера", "прокси-серверов")}.{Environment.NewLine + Environment.NewLine}{myServers}";
                                                if (userServers.Count == 0)
                                                {
                                                    var keyboardDo = new InlineKeyboardMarkup(
                                                        new[]
                                                        {
                                                            new[]
                                                            {
                                                                InlineKeyboardButton.WithCallbackData("Создать прокси-сервер", "create_server")
                                                            }
                                                        }
                                                    );
                                                    await Bot.SendTextMessageAsync(message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                                    return;
                                                }

                                                if (userServers.Count < 20)
                                                {
                                                    var keyboardDo = new InlineKeyboardMarkup(
                                                        new[]
                                                        {
                                                            new[]
                                                            {
                                                                InlineKeyboardButton.WithCallbackData("Удалить прокси-сервер", "delete_servers")
                                                            },
                                                            new[]
                                                            {
                                                                InlineKeyboardButton.WithCallbackData("Создать прокси-сервер", "create_server")
                                                            }
                                                        }
                                                    );
                                                    await Bot.SendTextMessageAsync(message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                                }
                                                else
                                                {
                                                    var keyboardDo = new InlineKeyboardMarkup(
                                                        new[]
                                                        {
                                                            new[]
                                                            {
                                                                InlineKeyboardButton.WithCallbackData("Удалить прокси-сервер", "delete_servers")
                                                            }
                                                        }
                                                    );
                                                    await Bot.SendTextMessageAsync(message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                                }
                                            }
                                            else
                                            {
                                                await Bot.AnswerCallbackQueryAsync(queryEventArgs.CallbackQuery.Id, "Ошибка получения списка прокси-серверов.", true);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        await Bot.AnswerCallbackQueryAsync(queryEventArgs.CallbackQuery.Id, "Ошибка удвления прокси-сервера.", true);
                                    }
                                }
                            }
                        }
                        break;

                    case "get_servers":
                        using (var dbOpen = new Db(DbConnStr))
                        {
                            var doData = dbOpen.Query($"SELECT ip, hash FROM DATA WHERE id_user = {userChatId}");
                            if (doData.Rows.Count > 0)
                            {
                                var row = doData.Rows[0];
                                var ip = row["ip"].ToString();
                                var hash = row["hash"].ToString();
                                await Bot.DeleteMessageAsync(message.Chat.Id, message.MessageId);
                                using (var request = new HttpRequest())
                                {
                                    var getServersPage = request.GET($"http://{ip}:1337/{hash}/getServers");
                                    if (getServersPage.IsOk)
                                    {
                                        var servers = JArray.Parse(getServersPage.Html);
                                        var userServers = JsonConvert.DeserializeObject<List<Server>>(servers.ToString());
                                        var possition = 0;
                                        var myServers = userServers.Aggregate("", (current, userServer) => current + string.Format("[[{4}]] [{0}:{1}](https://t.me/proxy?server={0}&port={1}&secret={2}){3}", userServer.Ip, userServer.Port, userServer.Secret, Environment.NewLine, ++possition));
                                        var messageDo =
                                            $"Все готово для развертки прокси-сервера! На данный момент у Вас {userServers.Count} {Declension(userServers.Count, "прокси-сервер", "прокси-сервера", "прокси-серверов")}.{Environment.NewLine + Environment.NewLine}{myServers}";
                                        if (userServers.Count == 0)
                                        {
                                            var keyboardDo = new InlineKeyboardMarkup(
                                                new[]
                                                {
                                                    new[]
                                                    {
                                                        InlineKeyboardButton.WithCallbackData("Создать прокси-сервер", "create_server")
                                                    }
                                                }
                                            );
                                            await Bot.SendTextMessageAsync(message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                            return;
                                        }

                                        if (userServers.Count < 20)
                                        {
                                            var keyboardDo = new InlineKeyboardMarkup(
                                                new[]
                                                {
                                                    new[]
                                                    {
                                                        InlineKeyboardButton.WithCallbackData("Удалить прокси-сервер", "delete_servers")
                                                    },
                                                    new[]
                                                    {
                                                        InlineKeyboardButton.WithCallbackData("Создать прокси-сервер", "create_server")
                                                    }
                                                }
                                            );
                                            await Bot.SendTextMessageAsync(message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                        }
                                        else
                                        {
                                            var keyboardDo = new InlineKeyboardMarkup(
                                                new[]
                                                {
                                                    new[]
                                                    {
                                                        InlineKeyboardButton.WithCallbackData("Удалить прокси-сервер", "delete_servers")
                                                    }
                                                }
                                            );
                                            await Bot.SendTextMessageAsync(message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                        }
                                    }
                                    else
                                    {
                                        await Bot.AnswerCallbackQueryAsync(queryEventArgs.CallbackQuery.Id, "Ошибка получения списка прокси-серверов.", true);
                                    }
                                }
                            }
                        }
                        break;

                    case "create_server":
                        using (var dbOpen = new Db(DbConnStr))
                        {
                            await Bot.DeleteMessageAsync(message.Chat.Id, message.MessageId);
                            var doData = dbOpen.Query($"SELECT ip, hash FROM DATA WHERE id_user = {userChatId}");
                            if (doData.Rows.Count > 0)
                            {
                                await Bot.AnswerCallbackQueryAsync(queryEventArgs.CallbackQuery.Id, "Ваш сервер создается. Это займет некоторое время.");
                                var row = doData.Rows[0];
                                var ip = row["ip"].ToString();
                                var hash = row["hash"].ToString();

                                using (var request = new HttpRequest())
                                {
                                    var getServersPage = request.GET($"http://{ip}:1337/{hash}/getServers");
                                    if (getServersPage.IsOk)
                                    {
                                        var servers = JArray.Parse(getServersPage.Html);
                                        var userServers = JsonConvert.DeserializeObject<List<Server>>(servers.ToString());
                                        if (userServers.Count >= 20)
                                        {
                                            const string messageDo = "Вы создали максимальное число прокси-серверов (20).";
                                            var keyboardDo = new InlineKeyboardMarkup(
                                                new[]
                                                {
                                                    new[]
                                                    {
                                                        InlineKeyboardButton.WithCallbackData("Список всех прокси-серверов", "get_servers")
                                                    }
                                                }
                                            );
                                            await Bot.SendTextMessageAsync(message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                            return;
                                        }
                                    }
                                }

                                using (var request = new HttpRequest())
                                {
                                    var сreateServer = request.GET($"http://{ip}:1337/{hash}/createServer");
                                    if (сreateServer.IsOk)
                                    {
                                        var servers = JArray.Parse(сreateServer.Html);
                                        var userServers = JsonConvert.DeserializeObject<List<Server>>(servers.ToString());
                                        var myServers = userServers.Aggregate("", (current, userServer) => current + string.Format("[{0}:{1}](https://t.me/proxy?server={0}&port={1}&secret={2}){3}", userServer.Ip, userServer.Port, userServer.Secret, Environment.NewLine));

                                        var messageDo =
                                            $"Прокси-сервер создан! Данные для подключения: {Environment.NewLine + Environment.NewLine}{myServers}";
                                        if (userServers.Count < 20)
                                        {
                                            var keyboardDo = new InlineKeyboardMarkup(
                                                new[]
                                                {
                                                    new[]
                                                    {
                                                        InlineKeyboardButton.WithCallbackData("Список всех прокси-серверов", "get_servers")
                                                    },
                                                    new[]
                                                    {
                                                        InlineKeyboardButton.WithCallbackData("Создать прокси-сервер", "create_server")
                                                    }
                                                }
                                            );
                                            await Bot.SendTextMessageAsync(message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                        }
                                        else
                                        {
                                            var keyboardDo = new InlineKeyboardMarkup(
                                               new[]
                                               {
                                                    new[]
                                                    {
                                                        InlineKeyboardButton.WithCallbackData("Список всех прокси-серверов", "get_servers")
                                                    }
                                               }
                                           );
                                            await Bot.SendTextMessageAsync(message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                        }
                                    }
                                    else
                                    {
                                        await Bot.AnswerCallbackQueryAsync(queryEventArgs.CallbackQuery.Id, "Ошибка создания прокси-сервера!", true);
                                    }
                                }
                            }
                        }
                        break;
                    case "get_droplets":
                        using (var dbOpen = new Db(DbConnStr))
                        {
                            var doData = dbOpen.Query(
                                $"SELECT do_access, do_droplet FROM DATA WHERE id_user = {userChatId}");
                            if (doData.Rows.Count > 0)
                            {
                                var row = doData.Rows[0];
                                var doToken = row["do_access"].ToString();
                                var doDroplet = row["do_droplet"].ToString();

                                var doClient = new DigitalOceanClient(doToken);
                                var droplet = doClient.Droplets.Get(int.Parse(doDroplet));
                                var intv4 = droplet.Result.Networks.V4.ToArray();
                            }
                        }
                        break;

                    case "create_droplet":
                        using (var dbOpen = new Db(DbConnStr))
                        {
                            await Bot.DeleteMessageAsync(message.Chat.Id, message.MessageId);
                            await Bot.AnswerCallbackQueryAsync(queryEventArgs.CallbackQuery.Id, "Началось создание дроплета. Процесс может занять некоторое время (обычно не более 5 минут).", true);

                            var doData = dbOpen.Query($"SELECT do_access FROM DATA WHERE id_user = {userChatId}");
                            if (doData.Rows.Count > 0)
                            {
                                var row = doData.Rows[0];
                                var doToken = row["do_access"].ToString();
                                var doClient = new DigitalOceanClient(doToken);

                                var request = new Droplet
                                {
                                    Name = $"TGOpen-{Guid.NewGuid().ToString()}",
                                    Region = DoRegions.PickRandom(),
                                    Size = "s-1vcpu-1gb",
                                    Image = "ubuntu-18-04-x64",
                                    SshKeys = new List<object>(),
                                    Backups = false,
                                    Ipv6 = false,
                                    Tags = new List<string> { "tgopen" },
                                    UserData = @"
                                        #cloud-config

                                        runcmd:
                                        - cd root && curl -L -o installer https://git.io/JeQq3 && chmod +x installer && ./installer
                                    ",
                                    PrivateNetworking = null,
                                    Volumes = new List<string>()
                                };

                                var droplet = await doClient.Droplets.Create(request);
                                dbOpen.NoneQuery(
                                    $"UPDATE DATA SET do_droplet = {droplet.Id} WHERE id_user = {userChatId}");
                                var completeDropletCreation = new BackgroundWorker { WorkerSupportsCancellation = true };

                                completeDropletCreation.DoWork += async delegate (object s, DoWorkEventArgs args)
                                {
                                    var worker = (BackgroundWorker)s;
                                    var start = DateTime.Now;
                                    do
                                    {
                                        Thread.Sleep(5000);
                                        var serv = await doClient.Droplets.Get(droplet.Id);
                                        if (serv.Status != "active") continue;
                                        var networks = serv.Networks.V4.ToArray();
                                        using (var hashRequest = new HttpRequest())
                                        {
                                            var isBlockedPage = hashRequest.GET(
                                                $"https://mod.ovh/dump/?host={networks[0].IpAddress}");
                                            if (isBlockedPage.IsOk)
                                            {
                                                JToken token = JObject.Parse(isBlockedPage.Html);
                                                var blocked = (bool)token.SelectToken("found");
                                                if (blocked)
                                                {
                                                    await doClient.Droplets.Delete(serv.Id);
                                                    dbOpen.NoneQuery(
                                                        $"UPDATE DATA SET do_droplet = '' WHERE id_user = {userChatId}");
                                                    const string messageDo = "Дроплет был удален, так как ему был присвоен заблокированный IP. Рекомендуется создать новый.";
                                                    var keyboardDo = new InlineKeyboardMarkup(
                                                        new[]
                                                        {
                                                            new[]
                                                            {
                                                                InlineKeyboardButton.WithCallbackData("Создать новый дроплет", "create_droplet")
                                                            }
                                                        }
                                                    );
                                                    await Bot.SendTextMessageAsync(message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                                    return;
                                                }
                                            }
                                        }
                                        break;
                                    }
                                    while ((DateTime.Now - start).TotalMinutes < 5 && !args.Cancel);

                                    do
                                    {
                                        try
                                        {
                                            var serv = await doClient.Droplets.Get(droplet.Id);
                                            var networks = serv.Networks.V4.ToArray();
                                            dbOpen.NoneQuery(
                                                $"UPDATE DATA SET ip = '{networks[0].IpAddress}' WHERE id_user = {userChatId}");
                                            using (var hashRequest = new HttpRequest())
                                            {
                                                var hashPage = hashRequest.GET(
                                                    $"http://{networks[0].IpAddress}:1337/getHash");
                                                if (hashPage.IsOk)
                                                {
                                                    worker.CancelAsync();
                                                    dbOpen.NoneQuery(
                                                        $"UPDATE DATA SET hash = '{hashPage.Html}' WHERE id_user = {userChatId}");
                                                    const string messageDo = "Все готово для развертки прокси-сервера!";
                                                    var keyboardDo = new InlineKeyboardMarkup(
                                                        new[]
                                                        {
                                                            new[]
                                                            {
                                                                InlineKeyboardButton.WithCallbackData("Создать прокси-сервер", "create_server")
                                                            }
                                                        }
                                                    );
                                                    await Bot.SendTextMessageAsync(message.Chat.Id, messageDo, ParseMode.Markdown, false, false, 0, keyboardDo);
                                                    return;
                                                }
                                            }
                                        }
                                        catch
                                        {
                                            // ignored
                                        }

                                        Thread.Sleep(5000);
                                    }
                                    while ((DateTime.Now - start).TotalMinutes < 5 && !args.Cancel);

                                    if (args.Cancel) return;
                                    dbOpen.NoneQuery(
                                        $"UPDATE DATA SET do_droplet = '', ip = '', hash = '' WHERE id_user = {userChatId}");
                                    const string mes = "Создание дроплета заняло слишком много времени. Задача отменена. Рекомендуется создать новый.";
                                    var kb = new InlineKeyboardMarkup(
                                        new[]
                                        {
                                            new[]
                                            {
                                                InlineKeyboardButton.WithCallbackData("Создать новый дроплет", "create_droplet")
                                            }
                                        }
                                    );
                                    await Bot.SendTextMessageAsync(message.Chat.Id, mes, ParseMode.Markdown, false, false, 0, kb);
                                };
                                completeDropletCreation.RunWorkerAsync();
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    await Bot.AnswerCallbackQueryAsync(queryEventArgs.CallbackQuery.Id, "Ошибка!" + Environment.NewLine + ex.Message, true);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    public static class EnumerableExtension
    {
        public static T PickRandom<T>(this IEnumerable<T> source)
        {
            return source.PickRandom(1).Single();
        }

        public static IEnumerable<T> PickRandom<T>(this IEnumerable<T> source, int count)
        {
            return source.Shuffle().Take(count);
        }

        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source)
        {
            return source.OrderBy(x => Guid.NewGuid());
        }
    }
}
