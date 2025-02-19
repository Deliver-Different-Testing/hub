using System;

namespace Hub.Models
{
    public class Contact
    {
        public int ClientId { get; set; }
        public string FirstName { get; set; }
        public string SurName { get; set; }
        public string? UserName { get; set; }
        public DateTime Created { get; set; } 
    }
}
