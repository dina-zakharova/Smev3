using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Smev3.Attributes;
using Smev3.Classes;
using Smev3.Classes.Smev3_1_2_test;
using Smev3.Enums;
using Smev3.EventArgs;
using Smev3.Helpers;
using Smev3.Interfaces;
using Smev3.Interfaces.Smev;
using Smev3.smev3_1_2_test;


namespace Smev3.Storages.SmevStorages
{
    //Класс вынесен с целью демонстрации 
    [SmevStorage(Schema = ExchangeSchema.Schema_1_2,
        Description = "Тестовая среда версия взаимодействия 1.2",
        ProductiveArea = false)]
    public class SmevStorageTest12 : SmevStorageBase
    {
        public SmevStorageTest12(string proxyAddress, string serialNumber) : base(proxyAddress, serialNumber)
        {
            DefaultNamespace = "urn://x-artefacts-smev-gov-ru/services/message-exchange/types/basic/1.2";

            FtpAddress = "ftp://smev3-n0.test.gosuslugi.ru";
        }

        protected static SMEVMessageExchangeService SmevService => new SMEVMessageExchangeService() {Proxy = Proxy};

        /// <summary>
        /// Получить запрос из очереди СМЭВ
        /// </summary>
        /// <returns>полученный запрос</returns>
        public override IRequest RecieveRequest()
        {
            var messageType = new MessageTypeSelector() {Id = ReferenceName};

            var signedMessage =
                CryptoProNetHelper.SignSmevObject(messageType, ReferenceName, DefaultNamespace, SerialNumber);

            var request = SmevService.GetRequest(messageType, signedMessage);

            if (request == null)
                return null;

            var message = SmevTestMessage.Create(request, out var ftpAttachments);

            if (ftpAttachments != null)
            {
                message.Attachments = RecieveFtpAttachments(ftpAttachments, message.MessageId).ToList<IAttachment>();
            }

            OnRequestRecieved(new RequestRecievedEventArgs()
            {
                Request = message
            });

            return message;
        }

        private static IEnumerable<FtpAttachment> RecieveFtpAttachments(IEnumerable<FSAuthInfo> attachments,
            Guid messageId)
        {
            var attachList = new List<FtpAttachment>();

            foreach (var attachment in attachments)
            {
                var attach = new FtpAttachment()
                {
                    MessageId = messageId,
                    FileName = attachment.FileName,
                    Password = attachment.Password,
                    UserName = attachment.UserName,
                    Uuid = attachment.uuid
                };

                var webRequest = new WebClient();
                var url = $"{FtpAddress}{attachment.FileName}";
                webRequest.Credentials = new NetworkCredential(attachment.UserName, attachment.Password);

                webRequest.Proxy = Proxy;

                attach.Document = webRequest.DownloadData(url);

                attachList.Add(attach);
            }

            return attachList;
        }

        /// <summary>
        /// Получить ответ из очереди СМЭВ
        /// </summary>
        /// <returns>ответ на запрос, отказ или статус запроса</returns>
        public override IMessage RecieveResponse()
        {
            var messageType = new MessageTypeSelector() {Id = ReferenceName};

            var signedMessage =
                CryptoProNetHelper.SignSmevObject(messageType, ReferenceName, DefaultNamespace, SerialNumber);

            var response = SmevService.GetResponse(messageType, signedMessage);

            if (response == null)
                return null;

            var message = SmevTestMessage.Create(response);

            switch (message)
            {
                case IResponse responseMessage:

                    OnResponseRecieved(new ResponseRecievedEventArgs()
                    {
                        Response = responseMessage
                    });

                    return responseMessage;

                case IReject reject:

                    OnErrorRecieved(new ErrorRecievedEventArgs()
                    {
                        ErrorMessage = new ErrorStatus()
                        {
                            MessageId = reject.MessageId,
                            Status = Status.Запрос_отклонен,
                            ErrorMessage = reject.RejectionMessage
                        }
                    });
                    return reject;

                case IStatusFault statusFault:

                    OnErrorRecieved(new ErrorRecievedEventArgs()
                    {
                        ErrorMessage = new ErrorStatus()
                        {
                            MessageId = statusFault.MessageId,
                            Status = Status.Ошибка_при_отправке,
                            ErrorMessage = statusFault.SmevFaultDescription
                        }
                    });

                    return statusFault;
            }

            return null;
        }

        /// <summary>
        /// Подтверждение получения сообщения и прохождения ФЛК
        /// </summary>
        /// <param name="ackMessage"></param>
        public override IMessageStatus Ack(IAck ackMessage)
        {
            var ack = new AckTargetMessage()
            {
                Id = ReferenceName,
                Accepted = ackMessage.IsValid,
                Value = ackMessage.MessageId.ToString()
            };

            var signedMessage = CryptoProNetHelper.SignSmevObject(ack, ReferenceName, DefaultNamespace, SerialNumber);

            MessageStatus status = null;

            Execute(delegate
            {
                SmevService.Ack(ack, signedMessage);

                status = new MessageStatus()
                {
                    MessageId = ackMessage.MessageId,
                    Status = ackMessage.IsValid ? Status.ФЛК_прошел_успешно : Status.Ошибки_ФЛК
                };
            }, ackMessage.MessageId);

            return status;
        }

        /// <summary>
        /// Отправка ответа на запрос
        /// </summary>
        /// <param name="response">ответ</param>
        /// <returns>статус ответа</returns>
        public override IMessageStatus SendResponse(IResponse response)
        {
            var responseData = new SenderProvidedResponseData
            {
                Id = ReferenceName,
                MessageID = response.MessageId.ToString(),
                To = response.ReplyTo,
                Items = new object[]
                {
                    response.MessageContent,
                },
                ItemsElementName = new[]
                {
                    ItemsChoiceType.MessagePrimaryContent
                }
            };

            var signedMessage = CryptoProNetHelper.SignSmevObject(responseData, ReferenceName, SerialNumber);

            IMessageStatus status = null;

            Execute(delegate
            {
                var metadata = SmevService.SendResponse(responseData, null, signedMessage, out _);

                status = SmevTestMessageStatus.Create(metadata);
            }, response.MessageId);

            return status;
        }

        public override IMessageStatus SendReject(IReject reject)
        {
            var responseData = new SenderProvidedResponseData
            {
                Id = ReferenceName,
                MessageID = reject.MessageId.ToString(),
                To = reject.ReplyTo,
                Items = new object[]
                {
                    new SenderProvidedResponseDataRequestRejected()
                    {
                        RejectionReasonDescription = reject.RejectionMessage,
                        RejectionReasonCode = (RejectCode) reject.RejectCode
                    },
                },
                ItemsElementName = new[]
                {
                    ItemsChoiceType.RequestRejected
                }
            };

            var signedMessage = CryptoProNetHelper.SignSmevObject(responseData, ReferenceName, SerialNumber);

            IMessageStatus status = null;

            Execute(delegate
            {
                var metadata = SmevService.SendResponse(responseData, null, signedMessage, out _);

                status = SmevTestMessageStatus.Create(metadata);
            }, reject.MessageId);

            return status;
        }

        public override IMessageStatus SendRequest(IRequest request)
        {
            var requestData = new SenderProvidedRequestData()
            {
                Id = ReferenceName,
                MessageID = request.MessageId.ToString(),
                MessagePrimaryContent = request.MessageContent,
                TestMessage = request.IsTest != null && request.IsTest.Value ? new smev3_1_2_test.Void() : null
            };

            var signedMessage = CryptoProNetHelper.SignSmevObject(requestData, ReferenceName, SerialNumber);

            IMessageStatus status = null;

            Execute(delegate
            {
                var metadata = SmevService.SendRequest(requestData, null, signedMessage, out _);

                status = SmevTestMessageStatus.Create(metadata);
            }, request.MessageId);

            return status;
        }
    }
}