using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using System.Xml;
using Queue;
using Queue.QueueMessages;
using Queue.Queues;
using Queue.Queues.MedSmev;
using SmevService.Properties;
using Timer = System.Timers.Timer;
using Smev3.Classes;
using Smev3.Enums;
using Smev3.EventArgs;
using Smev3.Helpers;
using Smev3.Interfaces.Log;
using Smev3.Interfaces.Smev;
using Smev3.Storages.LogStorage;
using Smev3.Storages.SmevStorages;
using SmevService.LogStorage;
using ErrorEventArgs = Smev3.EventArgs.ErrorEventArgs;

namespace SmevService
{
    public partial class SmevService : ServiceBase
    {
        private Timer _recieveRequestTimer;
        private Timer _recieveResponseTimer;
        private Timer _sendTimer;
        private AckQueue _ackQueue;
        private RejectQueue _rejectQueue;
        private ErrorsQueue _errorsQueue;

        private LogDatabaseStorage _databaseLog;
        private LogFileStorage _fileLog;
        private ISmevStorage _storage;

        public SmevService()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                _recieveRequestTimer = new Timer(Settings.Default.RECIEVE_REQUEST_TIMER * 1000);
                _recieveRequestTimer.Elapsed += RecieveRequestTimerOnElapsed;
                _recieveRequestTimer.Enabled = true;

                eventLog.WriteEntry($"Сервис запущен, периодичность проверки наличия запросов: {Settings.Default.RECIEVE_REQUEST_TIMER} сек",
                    EventLogEntryType.Information);

                _recieveResponseTimer = new Timer(Settings.Default.RECIEVE_RESPONSE_TIMER * 1000);
                _recieveResponseTimer.Elapsed += RecieveResponseTimerOnElapsed;
                _recieveResponseTimer.Enabled = true;

                eventLog.WriteEntry($"Сервис запущен, периодичность проверки наличия ответов: {Settings.Default.RECIEVE_RESPONSE_TIMER} сек",
                    EventLogEntryType.Information);

                _sendTimer = new Timer(Settings.Default.SEND_TIMER * 1000);
                _sendTimer.Elapsed += SendTimerOnElapsed;
                _sendTimer.Enabled = true;

                eventLog.WriteEntry($"Сервис запущен, периодичность проверки очереди для отправки: {Settings.Default.SEND_TIMER} сек",
                    EventLogEntryType.Information);

                _errorsQueue = new ErrorsQueue(Settings.Default.RABBIT_SERVER, Settings.Default.RABBIT_USER,
                    Settings.Default.RABBIT_PASS);

                _ackQueue = new AckQueue(Settings.Default.RABBIT_SERVER, Settings.Default.RABBIT_USER,
                    Settings.Default.RABBIT_PASS);
                _ackQueue.LogEvent += QueueOnLogEvent;
                _ackQueue.Subscribe(OnReceivedAck, OnErrorAck, true, Settings.Default.WAITING_MIN,
                    Settings.Default.MAX_ITERATIONS);

                _rejectQueue = new RejectQueue(Settings.Default.RABBIT_SERVER, Settings.Default.RABBIT_USER,
                    Settings.Default.RABBIT_PASS);
                _rejectQueue.LogEvent += QueueOnLogEvent;
                _rejectQueue.Subscribe(OnReceivedReject, OnErrorReject, true, Settings.Default.WAITING_MIN,
                    Settings.Default.MAX_ITERATIONS);
            }
            catch (Exception ex)
            {
                eventLog.WriteEntry(ex.Message, EventLogEntryType.Error);
                Stop();
            }
        }

        private void QueueOnLogEvent(object sender, QueueLogEventArgs logEventArgs)
        {
            _errorsQueue.Push(logEventArgs);
        }

        private void OnErrorAck(AckQueueMessage queueMessage)
        {
            eventLog.WriteEntry($"Ошибка при подтверждении сообщения с messageId: {queueMessage.MessageId}", EventLogEntryType.Information);
        }

        private void OnReceivedAck(AckQueueMessage queueMessage, CancellationToken cancellationToken)
        {
            Execute(() =>
            {
                var status = _storage.Ack(new Ack
                {
                    MessageId = Guid.Parse(queueMessage.MessageId),
                    IsValid = queueMessage.Valid
                });

                if (status != null)
                {
                    _databaseLog.UpdateStatus(status);
                }

                eventLog.WriteEntry($"Подтверждено получение сообщения с messageId {queueMessage.MessageId}",
                    EventLogEntryType.Information);
            });
        }

        private void OnErrorReject(RejectMessage queueMessage)
        {
            eventLog.WriteEntry($"Ошибка при отклонении сообщения с messageId {queueMessage.MessageId}", EventLogEntryType.Information);
        }

        private void OnReceivedReject(RejectMessage queueMessage, CancellationToken cancellationToken)
        {
            Execute(() =>
            {
                var rejectCode = (RejectionCode) queueMessage.SmevRejectCode;

                var parentMessage = _databaseLog.GetMessage(Guid.Parse(queueMessage.MessageId));

                var status = _storage.Reject(rejectCode, parentMessage);

                if (status != null)
                {
                    _databaseLog.UpdateStatus(status);
                }

                eventLog.WriteEntry($"Отклонено сообщение с messageId {queueMessage.MessageId}",
                    EventLogEntryType.Information);
            });
        }

        private void Execute(Action action)
        {
            try
            {
                _databaseLog = new LogDatabaseStorage();

                _fileLog = new LogFileStorage(Settings.Default.STORAGE_FOLDER);


                _storage = SmevStorageBase.Create(Settings.Default.IS_PRODUCTIVE_AREA, ExchangeSchema.Schema_1_2,
                    Settings.Default.PROXY_ADDRESS, Settings.Default.SERIAL_NUMBER);

                _storage.Error += StorageOnError;

                action();
            }
            catch (Exception ex)
            {
                eventLog.WriteEntry(ex.Message, EventLogEntryType.Error);
            }
            finally
            {
                _databaseLog.Dispose();
                _fileLog.Dispose();
            }

        }

        protected void RecieveRequestTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            _recieveRequestTimer.Enabled = false;

            Execute(() =>
            {
                _storage.RequestRecieved += StorageOnRequestRecieved;

                _storage.RecieveRequest();
            });

            _recieveRequestTimer.Enabled = true;
        }

        private void StorageOnRequestRecieved(object sender, RequestRecievedEventArgs requestRecievedEventArgs)
        {
            var request = requestRecievedEventArgs.Request;

            eventLog.WriteEntry($"Получен запрос с messageId {request.MessageId}", EventLogEntryType.Information);

            Execute(() =>
            {
                _databaseLog.SaveMessage(request);

                _databaseLog.UpdateStatus(new MessageStatus()
                {
                    MessageId = request.MessageId,
                    Status = Status.Получен_запрос
                });

                _fileLog.SaveMessage(request);

            });


            if (request.Attachments != null)
            {
                foreach (var attachment in request.Attachments)
                {
                    eventLog.WriteEntry(
                        $"Получен файл с приложением {((FtpAttachment) attachment).Uuid}, {((FtpAttachment) attachment).UserName}, {((FtpAttachment) attachment).Password}, {((FtpAttachment) attachment).FileName}",
                        EventLogEntryType.Information);
                }
            }
        }

        protected void RecieveResponseTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            _recieveResponseTimer.Enabled = false;

            Execute(() =>
            {
                _storage.ResponseRecieved += StorageOnResponseRecieved;

                _storage.ErrorRecieved += StorageOnErrorRecieved;

                _storage.RecieveResponse();
            });

            _recieveResponseTimer.Enabled = true;
        }

        private void StorageOnResponseRecieved(object sender, ResponseRecievedEventArgs responseRecievedEventArgs)
        {
            var response = responseRecievedEventArgs.Response;

            Execute(() =>
            {
                if (response.OriginalMessageId != null)
                    _databaseLog.UpdateStatus(new MessageStatus()
                    {
                        MessageId = response.OriginalMessageId.Value,
                        Status = Status.Получен_ответ
                    });

                _databaseLog.SaveMessage(response);

                _databaseLog.UpdateStatus(new MessageStatus()
                {
                    MessageId = response.MessageId,
                    Status = Status.Получен_ответ
                });

                _fileLog.SaveMessage(response);
            });

            eventLog.WriteEntry($"Получен ответ с messageId {response.MessageId}", EventLogEntryType.Information);

        }

        private void StorageOnErrorRecieved(object sender, ErrorRecievedEventArgs errorRecievedEventArgs)
        {
            if (errorRecievedEventArgs.ErrorMessage is IErrorMessage message)
            {
                Execute(() =>
                {
                    _databaseLog.SetError(message);
                });

                eventLog.WriteEntry(
                    $"{message.Status}, messageId:{message.MessageId}, {message.ErrorMessage}",
                    EventLogEntryType.Information);

                _ackQueue.Push(new AckQueueMessage()
                {
                    MessageId = message.MessageId.ToString(),
                    Valid = true
                });
            }
        }

        private void StorageOnError(object sender, ErrorEventArgs errorEventArgs)
        {
            Execute(() =>
            {
                _databaseLog.SetError(errorEventArgs.ErrorMessage);
            });
        }

        protected void SendTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            _sendTimer.Enabled = false;

            Execute(() =>
            {
                var files = Directory.GetFiles(Settings.Default.SMEV_FOLDER);

                foreach (var file in files)
                {
                    if (FileHelper.FileIsBusy(file))
                    {
                        continue;
                    }

                    var filename = new FileInfo(file).Name.ToUpper();

                    var fs = new MemoryStream(File.ReadAllBytes(file));

                    var doc = new XmlDocument();

                    doc.Load(fs);

                    var match = Regex.Match(filename, @"^(?<prefix>\S+)_OUT_(?<messageId>\S{36})(?<test>\S+).XML$");

                    if (!match.Success)
                    {
                        File.Delete(file);

                        throw new Exception($"Файл {filename} не подходит для загрузки в СМЭВ");
                    }

                    var messageId = Guid.Parse(match.Groups["messageId"].ToString());

                    _storage.MessageSent += StorageOnMessageSent;

                    var originalMessage = _databaseLog.GetMessage(messageId);

                    if (originalMessage != null)
                    {
                        _storage.Send(doc.DocumentElement, originalMessage, messageId);
                    }
                    else
                    {
                        _storage.Send(doc.DocumentElement, messageId, match.Groups["test"].ToString()=="_TEST");
                    }

                    File.Delete(file);
                }
            });

            _sendTimer.Enabled = true;
        }

        private void StorageOnMessageSent(object sender, MessageSentEventArgs messageSentEventArgs)
        {
            Execute(() =>
            {
                _databaseLog.SaveMessage(messageSentEventArgs.Message);

                if (messageSentEventArgs.MessageStatus != null)
                {
                    _databaseLog.UpdateStatus(messageSentEventArgs.MessageStatus);
                }
            });

            eventLog.WriteEntry($"Сообщение с messageId {messageSentEventArgs.MessageStatus.MessageId} отправлен",
                    EventLogEntryType.Information);
        }

        protected override void OnStop()
        {
            _recieveRequestTimer.Enabled = false;
            _recieveResponseTimer.Enabled = false;
            _sendTimer.Enabled = false;
            _rejectQueue.Dispose();
            _ackQueue.Dispose();
            _errorsQueue.Dispose();
        }
    }
}