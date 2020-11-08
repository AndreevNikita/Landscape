using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL;
using OpenTK;

namespace Landscape
{
    static class Program
    {
        [STAThread]
        static void Main() {
            Application.Run(new MainWindow());
        }
    }
}
