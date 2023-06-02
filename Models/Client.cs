using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UrgentHub.Models
{
    public class Client
    {
        public string Name { get; set; }
        public bool Active { get; set; }
        public bool Internal { get; set; }
        public string Code { get; set; }
        public string WebServicePassword { get; set; }
        public DateTime Created { get; set; }
        public bool StripeClient { get; set; }
    }
}
