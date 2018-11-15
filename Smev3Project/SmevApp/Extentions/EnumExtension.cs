using System;

namespace SmevApp.Extentions
{
    public static class EnumExtension
    {
        public static string ToStringRus(this Enum e)
        {
            return e.ToString().Replace("_", " ");
        }
    }
}