using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DbLib.Models
{
    public class Attachment
    {
        public Guid Id { get; set; }

        public Guid MessageId { get; set; }

        public string FileName { get; set; }

        public byte[] Document { get; set; }
    }
}
