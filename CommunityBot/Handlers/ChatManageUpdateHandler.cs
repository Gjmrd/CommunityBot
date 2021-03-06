using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityBot.Contracts;
using CommunityBot.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Chat = Telegram.Bot.Types.Chat;

namespace CommunityBot.Handlers
{
    public class ChatManageUpdateHandler : UpdateHandlerBase
    {
        private readonly IChatRepository _chatRepository;
        private const string AddChatCommand = "add_chat";
        private const string AddThisChatCommand = "add_this_chat";
        private const string RemoveChatCommand = "remove_chat";
        private const string GetIdOfThisChat = "get_id_of_this_chat";
        
        public ChatManageUpdateHandler(
            ITelegramBotClient botClient, 
            IOptions<BotConfigurationOptions> options,
            IChatRepository chatRepository,
            ILogger<ChatManageUpdateHandler> logger) 
            : base(botClient, options, logger)
        {
            _chatRepository = chatRepository;
        }

        protected override UpdateType[] AllowedUpdates => new[] {UpdateType.Message};

        protected override bool CanHandle(Update update)
        {
            return new[] {AddChatCommand, AddThisChatCommand, RemoveChatCommand, GetIdOfThisChat}
                .Contains(update.Message.GetFirstBotCommand()?.name);
        }

        protected override async Task HandleUpdateInternalAsync(Update update)
        {
            var command = update.Message.GetFirstBotCommand()!.Value;

            if (command.name == AddChatCommand)
            {
                await AddChat(command.arg, update.Message.Chat.Id, update.Message.MessageId);
                return;
            }

            if (command.name == AddThisChatCommand)
            {
                await AddThisChat(command.arg, update.Message.Chat, update.Message.MessageId);
                return;
            }

            if (command.name == RemoveChatCommand)
            {
                await RemoveChat(command.arg, update.Message.From.Username, update.Message.Chat.Id, update.Message.MessageId);
                return;
            }

            if (command.name == GetIdOfThisChat)
            {
                await SendMessage(update.Message.Chat.Id, $"ID этого чата: {update.Message.Chat.Id}", update.Message.MessageId);
            }
        }

        private async Task SendMessage(long replyChatId, string text, int replyToMessageId)
        {
            await BotClient.SendTextMessageAsync(replyChatId, text, replyToMessageId: replyToMessageId);
        }

        private async Task AddChat(string chatRawArgs, long replyChatId, int replyToMessageId)
        {
            var arg = chatRawArgs.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            if (arg.Length < 2)
            {
                await SendMessage(replyChatId, "Неправильно отправленная команда. Пожалуйста попробуйте ещё раз или обратитесь к админам за помощью.", replyToMessageId);
                return;
            }

            if (!arg[1].StartsWith("https://t.me/joinchat/"))
            {
                await SendMessage(
                    replyChatId,
                    "Неправильная ссылка приглашение: ссылка должна начинаться с 'https://t.me/joinchat/'. Добавлять ссылку на публичные чаты не нужно.",
                    replyToMessageId);
                return;
            }
                
            var chatName = arg[0];
            var chatLink = arg[1];

            await _chatRepository.AddOrUpdate(new SavedChat(-1, chatName, chatLink));

            await SendMessage(replyChatId, "Чат добавлен/обновлён! Спасибо за помощь боту!", replyToMessageId);
        }

        private async Task AddThisChat(string inviteLink, Chat chat, int replyToMessageId)
        {
            if (chat.IsPrivate())
            {
                await SendMessage(chat.Id, "Зачем ты пытаешься добавить наш личный чат в список чатов? >_>", replyToMessageId);
                return;
            }

            if (!chat.IsGroup())
            {
                return;
            }

            if (inviteLink.IsBlank() && chat.InviteLink.IsBlank())
            {
                try
                {
                    chat.InviteLink = await BotClient.ExportChatInviteLinkAsync(chat.Id);
                }
                catch (ApiRequestException e)
                {
                    Logger.LogWarning("Can't get invite link for chat {chatId}! [ExMessage: {exMessage}, StackTrace: {stackTrace}]", chat.Id, e.Message, e.StackTrace);
                }

                if (chat.InviteLink.IsBlank())
                {
                    await SendMessage(chat.Id, "Или дайте мне ссылку-приглашение вместе с коммандой, или сделайте админом, чтобы я сам мог создать её.", replyToMessageId);
                    return;
                }
            }

            await _chatRepository.AddOrUpdate(new SavedChat(chat.Id, chat.Title, chat.InviteLink));
                
            await SendMessage(chat.Id, "Чат добавлен! Спасибо за помощь боту!", replyToMessageId);
        }

        private async Task RemoveChat(string chatExactName, string fromUserName, long replyChatId, int replyToMessageId)
        {
            if (!Options.Admins.Contains(fromUserName))
            {
                await SendMessage(replyChatId, "Если хочещь удалить чат из моего списка, то попроси админов.", replyToMessageId);
                return;
            }

            if (chatExactName.IsBlank())
            {
                await SendMessage(replyChatId, "Напиши рядом с командой полное имя чата, который удаляем.", replyToMessageId);
                return;
            }

            await _chatRepository.RemoveByName(chatExactName);

            await SendMessage(replyChatId, $"Если чат с названием {chatExactName} существовал в моём списке, то я его удалил.", replyToMessageId);
        }
    }
}