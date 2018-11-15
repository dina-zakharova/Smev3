namespace SmevApp.Extentions
{
    public class ForEachOnSelectedRowsSettings
    {
        /// <summary>
        /// Для обработки операции должна быть выбрана одна строка
        /// </summary>
        public bool OnlyOneRowOperation = false;

        /// <summary>
        /// Название операции (глагол)
        /// </summary>
        public string OperationName;

        /// <summary>
        /// ОБновить форму после завершения операции
        /// </summary>
        public bool Refresh = false;


        /// <summary>
        /// Показывать ProgressBar
        /// </summary>
        public bool ShowProgressBar = true;
    }
}
