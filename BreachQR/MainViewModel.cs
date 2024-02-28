﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BreachQR
{
    public partial class MainViewModel
    {
        private string fileName;
        public string FileName
        {
            set { fileName = value; }
            get { return fileName; }
        }

        private long fileBytes;
        public long FileBytes
        {
            set { fileBytes = value; }
            get { return fileBytes; }
        }
        public MainViewModel() 
        {
            string[] arguments = Environment.GetCommandLineArgs();
            Console.WriteLine("GetCommandLineArgs: {0}", string.Join(", ", arguments));
            if (arguments.Length > 1 )
            {
                FileInfo fileInfo = new FileInfo(arguments[1]);

                FileBytes = fileInfo.Length;
                FileName = fileInfo.Name;
            }
            else
            {
                FileName = arguments[0];
            }
        }
    }
}
