using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DbLib.Models;
using DevExpress.Data;
using DevExpress.Xpf.Bars;
using DevExpress.Xpf.Grid;
using Microsoft.Win32;
using Queue.QueueMessages;
using Queue.Queues.MedSmev;
using SmevApp.Extentions;
using SmevApp.Properties;
using Message = DbLib.Models.Message;
using Path = System.IO.Path;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace SmevApp
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            RefreshGridData();
        }

        #region Common
        //C:\Users\UserName\AppData\Roaming\SmevApp\GridLayouts
        private static string LayoutFolder =>
            $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\SmevApp\\GridLayouts";

        private string LayoutName => Path.Combine(LayoutFolder, $"{GetType().FullName}.lay");

        /// <summary>
        /// Обновление данных в grid
        /// </summary>
        protected async void RefreshGridData()
        {
            var selected = GridControl.SelectedItems;
            var topGridRow = GridControl.View.TopRowIndex;
            if (GridControl.ItemsSource != null)
            {
                SaveCurrentLayout();
            }

            GridControl.BeginDataUpdate();

            GridControl.ItemsSource = await ExecAsync(GetData, CancellationToken.None);

            GridControl.EndDataUpdate();

            RestoreLayout();
            GridControl.View.Focus();
            GridControl.SelectedItems = selected;
            GridControl.View.TopRowIndex = topGridRow;
        }

        /// <summary>
        /// Сохранение layout
        /// </summary>
        private void SaveCurrentLayout()
        {
            try
            {
                if (!Directory.Exists(LayoutFolder))
                {
                    Directory.CreateDirectory(LayoutFolder);
                }

                if (GridControl.ItemsSource != null)
                {
                    GridControl.SaveLayoutToXml(LayoutName);
                }
            }
            catch
            {
                // ignored
            }
        }

        /// <summary>
        /// Подгрузка layout
        /// </summary>
        private void RestoreLayout()
        {
            try
            {
                if (!Directory.Exists(LayoutFolder))
                {
                    return;
                }

                GridControl.RestoreLayoutFromXml(LayoutName);
            }
            catch
            {
                // ignored
            }
        }

        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            if (GridControl.ItemsSource != null)
            {
                SaveCurrentLayout();
            }
        }

        private void GridControl_OnCustomColumnDisplayText(object sender, CustomColumnDisplayTextEventArgs e)
        {
            if (!(e.Value is Enum))
            {
                return;
            }

            e.DisplayText = ((Enum) e.Value).ToStringRus();
        }

        protected async Task ForeachOnSelectedRows<TRowType>(Action<TRowType, CancellationToken> operation,
            ForEachOnSelectedRowsSettings settings) where TRowType : class
        {
            Progress progress = null;

            var totalCount = GridControl.SelectedItems.Count;

            if (totalCount == 0)
            {
                throw new Exception("Не выбрано ни одной строки");
            }

            if (settings.OnlyOneRowOperation&&totalCount>1)
            {
                throw new Exception("Выберите только одну строку!");
            }

            try
            {
                if (settings.ShowProgressBar)
                {
                    progress = ShowProgress(settings.OperationName, totalCount);
                }

                foreach (TRowType row in GridControl.SelectedItems)
                {
                    if (row == null)
                    {
                        throw new Exception("Не удалось преобразовать строку таблицы к типу " + typeof(TRowType).Name);
                    }

                    var token = progress?.CancellationTokenSource.Token ?? CancellationToken.None;

                    await ExecAsync(() => { operation(row, token); }, token);

                    token.ThrowIfCancellationRequested();

                    progress?.IncProgress();
                }

                if (settings.Refresh)
                {
                    RefreshGridData();
                }

                progress?.Finish();
            }
            catch (Exception)
            {
                progress?.Close();

                throw;
            }
        }

        protected Progress ShowProgress(string operationName, int totalCount)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            var progress = new Progress(operationName, cancellationTokenSource, totalCount);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                progress.ShowDialog();
            }));
            return progress;
        }

        protected async Task ExecAsync(Action operation, CancellationToken cancellationToken)
        {
            await ExecAsync<object>(() =>
            {
                operation();
                return null;
            }, cancellationToken);
        }

        protected async Task<TResult> ExecAsync<TResult>(Func<TResult> operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var task = GetTask(operation, cancellationToken);

            await task;

            return task.Result;
        }

        protected Task<TResult> GetTask<TResult>(Func<TResult> operation, CancellationToken cancellationToken)
        {
            return Task.Run(operation, cancellationToken);
        }

        private void GridControl_OnCustomSummary(object sender, CustomSummaryEventArgs e)
        {
            if (((GridSummaryItem)e.Item).FieldName != "Id")
                return;

            if (e.IsTotalSummary && e.SummaryProcess == CustomSummaryProcess.Finalize)
            {
                e.TotalValue = GridControl.SelectedItems.Count;
            }
        }

        private void GridControl_OnSelectionChanged(object sender, GridSelectionChangedEventArgs e)
        {
            GridControl.UpdateTotalSummary();
        }

        #endregion


        public object GetData()
        {
            var db = new SmevContext();

            return db.Messages.ToList();
        }

        private async void BtnAck_OnItemClick(object sender, ItemClickEventArgs e)
        {
            await ForeachOnSelectedRows(delegate (Message message, CancellationToken token)
            {
                if (!message.NeedAck)
                {
                    return;
                }

                var ackQueue = new AckQueue(Settings.Default.RABBIT_SERVER, Settings.Default.RABBIT_USER, Settings.Default.RABBIT_PASS);

                ackQueue.Push(new AckQueueMessage()
                {
                    MessageId = message.MessageId.ToString(),
                    Valid = true
                });

            }, new ForEachOnSelectedRowsSettings()
            {
                OperationName = e.Item.Hint.ToString(),
                Refresh = true
            });
        }

        private async void BtnDelete_OnItemClick(object sender, ItemClickEventArgs e)
        {
            await ForeachOnSelectedRows(delegate (Message message, CancellationToken token)
            {
                var db = new SmevContext();
                db.Messages.Remove(message);

            }, new ForEachOnSelectedRowsSettings()
            {
                OperationName = e.Item.Hint.ToString(),
                Refresh = true
            });
        }

        private async void BtnDownload_OnItemClick(object sender, ItemClickEventArgs e)
        {
            await ForeachOnSelectedRows(delegate (Message message, CancellationToken token)
            {
                var db = new SmevContext();
                var vidSved = db.VidSveds.FirstOrDefault(i => i.NamespaceUri == message.NamespaceUri);

                if (vidSved == null)
                    throw new Exception("Вид сведений не существует");

                var dialog = new SaveFileDialog
                {
                    FileName = $"{vidSved.Prefix}_{(message.IsIncome ? "IN" : "OUT")}_{message.MessageId}.XML",
                    Filter = @"Файлы xml|*.xml",
                    FilterIndex = 0
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllBytes(dialog.FileName, message.MessageContent);
                }

            }, new ForEachOnSelectedRowsSettings()
            {
                OperationName = e.Item.Hint.ToString(),
                OnlyOneRowOperation = true,
                Refresh = false,
                ShowProgressBar = false
            });
        }

        private void BtnSendRequest_OnItemClick(object sender, ItemClickEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void BtnSendResponse_OnItemClick(object sender, ItemClickEventArgs e)
        {
            throw new NotImplementedException();
        }

        private async void BtnSendReject_OnItemClick(object sender, ItemClickEventArgs e)
        {
            await ForeachOnSelectedRows(delegate(Message message, CancellationToken token)
            {
                var rejected = new RejectQueue(Settings.Default.RABBIT_SERVER, Settings.Default.RABBIT_USER,
                    Settings.Default.RABBIT_PASS);

                rejected.Push(new RejectMessage()
                {
                    MessageId = message.MessageId.ToString(),
                    SmevRejectCode = 0
                });

            }, new ForEachOnSelectedRowsSettings()
            {
                OperationName = e.Item.Hint.ToString(),
                Refresh = true
            });
        }

        private void BtnVidSved_OnItemClick(object sender, ItemClickEventArgs e)
        {
            throw new NotImplementedException();
        }
    }
}
