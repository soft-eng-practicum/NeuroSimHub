﻿using System.ComponentModel.DataAnnotations;

namespace AnalySim.Models
{
    public class UserLoginViewModel
    {
        [Required]
        [Display(Name="User Name")]
        public string Username { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }
}
