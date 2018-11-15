using System;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using DbLib.Enums;
using DbLib.Models;
using Smev3.Classes;
using Smev3.Helpers;
using Smev3.Interfaces;
using Smev3.Interfaces.Log;
using Smev3.Interfaces.Smev;

namespace Smev3.Storages.LogStorage
{
    //Класс вынесен с целью демонстрации 
    public class LogDatabaseStorage : ILogSmevStorage
    {
        public LogDatabaseStorage()
        {
            Db = new SmevContext();
        }

        private SmevContext Db { get; set; }

        /// <summary>
        /// Сохранить сообщение СМЭВ
        /// </summary>
        /// <param name="message">Запрос</param>
        public void SaveMessage(IRequest message)
        {
            var mes = new Message()
            {
                NamespaceUri = message.NamespaceUri,
                MessageType = (MessageType)message.MessageType,
                ReplyTo = message.ReplyTo,
                MessageId = message.MessageId,
                OriginalMessageId = message.OriginalMessageId
            };

            using (var stream = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(stream,
                    new XmlWriterSettings()
                    {
                        Indent = false,
                        CloseOutput = true,
                        Encoding = Encoding.GetEncoding("Windows-1251")
                    }))
                {
                    message.MessageContent.WriteTo(writer);
                }

                mes.MessageContent = stream.ToArray();
            }

            Db.Messages.Add(mes);
            Db.SaveChanges();

            if (message.Attachments != null)
            {
                foreach (var attach in message.Attachments)
                {
                    Db.Attachments.Add(new Attachment()
                    {
                        MessageId = attach.MessageId,
                        FileName = FileHelper.GetValidFilename(attach.FileName),
                        Document = attach.Document
                    });
                }

                Db.SaveChanges();
            }
        }

        /// <summary>
        /// Сохранить сообщение СМЭВ
        /// </summary>
        /// <param name="message">Ответ</param>
        public void SaveMessage(IResponse message)
        {
            var mes = new Message()
            {
                NamespaceUri = message.NamespaceUri,
                MessageType = (MessageType)message.MessageType,
                ReplyTo = message.ReplyTo,
                MessageId = message.MessageId,
                OriginalMessageId = message.OriginalMessageId
            };

            using (var stream = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(stream,
                    new XmlWriterSettings()
                    {
                        Indent = false,
                        CloseOutput = true,
                        Encoding = Encoding.GetEncoding("Windows-1251")
                    }))
                {
                    message.MessageContent.WriteTo(writer);
                }

                mes.MessageContent = stream.ToArray();
            }

            Db.Messages.Add(mes);
            Db.SaveChanges();
        }

        /// <summary>
        /// Сохранить сообщение СМЭВ
        /// </summary>
        /// <param name="message">Сообщение</param>
        public void SaveMessage(IMessage message)
        {
            switch (message)
            {
                case IResponse response:
                    SaveMessage(response);
                    return;

                case IRequest request:
                    SaveMessage(request);
                    return;

                default:
                    var mes = new Message()
                    {
                        NamespaceUri = message.NamespaceUri,
                        MessageType = (MessageType)message.MessageType,
                        ReplyTo = message.ReplyTo,
                        MessageId = message.MessageId,
                        OriginalMessageId = message.OriginalMessageId
                    };

                    Db.Messages.Add(mes);
                    Db.SaveChanges();
                    return;
            }
        }

        /// <summary>
        /// Обновить статус сообщения
        /// </summary>
        /// <param name="messageStatus">Статус сообщения</param>
        public void UpdateStatus(IMessageStatus messageStatus)
        {
            var message = Db.Messages.FirstOrDefault(i => i.MessageId == messageStatus.MessageId);

            if (message == null)
                return;

            message.Status = (Status) messageStatus.Status;
            Db.Entry(message).State = EntityState.Modified;
            Db.SaveChanges();
        }

        /// <summary>
        /// Сохраниь ошибку для сообщения
        /// </summary>
        /// <param name="error">Ошибка</param>
        public void SetError(IErrorMessage error)
        {
            var message = Db.Messages.FirstOrDefault(i => i.MessageId == error.MessageId);

            if (message == null)
                return;

            message.Status = (Status)error.Status;
            message.ErrorException = error.ErrorMessage;
            Db.Entry(message).State = EntityState.Modified;
            Db.SaveChanges();
        }

        /// <summary>
        /// Получить сообщение из хранилища
        /// </summary>
        /// <param name="messageId"></param>
        /// <returns></returns>
        public IMessage GetMessage(Guid messageId)
        {
            var message = Db.Messages.FirstOrDefault(i => i.MessageId == messageId);

            if (message == null)
                return null;

            var doc = new XmlDocument();
            doc.Load(new MemoryStream(message.MessageContent));

            switch (message.MessageType)
            {
                case MessageType.Входящий_ответ:
                case MessageType.Исходящий_ответ:
                    return new Response()
                    {
                        MessageType = (Smev3.Enums.MessageType)message.MessageType,
                        MessageId = messageId,
                        MessageContent = doc.DocumentElement,
                        NamespaceUri = message.NamespaceUri,
                        OriginalMessageId = message.OriginalMessageId,
                        ReplyTo = message.ReplyTo
                    };

                case MessageType.Входящий_запрос:
                case MessageType.Исходящий_запрос:
                    return new Request()
                    {
                        MessageType = (Smev3.Enums.MessageType)message.MessageType,
                        MessageId = messageId,
                        MessageContent = doc.DocumentElement,
                        NamespaceUri = message.NamespaceUri,
                        OriginalMessageId = message.OriginalMessageId,
                        ReplyTo = message.ReplyTo
                    };
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void Dispose()
        {
            Db?.Dispose();
        }
    }
}
