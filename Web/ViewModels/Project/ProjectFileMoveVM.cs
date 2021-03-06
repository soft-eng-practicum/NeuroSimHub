﻿using System.ComponentModel.DataAnnotations;

namespace Web.ViewModels.Project
{
    public class ProjectFileMoveVM
    {
        [Required(ErrorMessage = "File ID is a required field.")]
        public int FileID { get; set; }

        [Required(ErrorMessage = "Sub Directory is a required field.")]
        [Display(Name = "Sub Directory")]
        public string SubDirectory { get; set; }

    }
}
