using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DbLib.Models
{
    public class VidSved
    {
        public int Id { get; set; }

        public string Caption { get; set; } 

        public string NamespaceUri { get; set; }

        public string Prefix { get; set; }
    }
}
