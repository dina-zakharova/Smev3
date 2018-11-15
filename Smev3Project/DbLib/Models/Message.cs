using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DbLib.Enums;


namespace DbLib.Models
{
    public class Message
    {
        public int Id { get; set; }

        public Guid MessageId { get; set; }

        [Display(Name = @"Текст сообщения")]
        public byte[] MessageContent { get; set; }

        [Display(Name = @"Тип сообщения")]
        public MessageType MessageType { get; set; }

        [Display(Name = @"Статус")]
        public Status Status { get; set; }

        [Display(Name = @"Адрес ответа")]
        public string ReplyTo { get; set; }

        public Guid? OriginalMessageId { get; set; }

        [Display(Name = @"Прошел ФЛК")]
        public bool? IsValid { get; set; }

        [Display(Name = @"Текст ошибки")]
        public string ErrorException { get; set; }

        [Display(Name = @"Namespace")]
        public string NamespaceUri { get; set; }

        [Display(Name = @"Дата создания")]
        public DateTime CreateDate { get; set; }

        public bool NeedAck => (MessageType == MessageType.Входящий_запрос ||
                                MessageType == MessageType.Входящий_ответ) &&
                               (Status == Status.Получен_запрос ||
                                Status == Status.Получен_ответ);

        public bool IsIncome => MessageType == MessageType.Входящий_запрос || MessageType == MessageType.Входящий_ответ;
    }
}
