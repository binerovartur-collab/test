using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace Ezcad_Funtion_Show
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

   
        private void button1_Click(object sender, EventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            dlg.Filter = "Ezd files (*.ezd)|*.ezd";
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                textBox1.Text = dlg.FileName;
                if (MarkEzdDll.LoadEzdFile(textBox1.Text) != 0)
                {
                    MessageBox.Show("打开Ezd文件" + textBox1.Text + "失败!");
                }
                ShowPreviewBmp();
            }
        }


        public void ShowPreviewBmp()
        {
            int w = pictureBox1.Size.Width;
            int h = pictureBox1.Size.Height;
            if (w > h)
            {
                w = h;
            }
            else
            {
                h = w;
            }
            
            pictureBox1.Image = MarkEzdDll.GetCurPreviewImage(w, h);
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                String dir = Application.StartupPath;
                int nErr = MarkEzdDll.Initialize(dir, false);
                MessageBox.Show("Initial="+nErr.ToString());
                
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                this.Close();
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (!backgroundWorker1.IsBusy)
            {
                backgroundWorker1.RunWorkerAsync();
            }
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            int nErr = MarkEzdDll.Mark(false);
            MessageBox.Show("Mark=" + nErr.ToString());
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            MessageBox.Show("Finish");
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            MarkEzdDll.Close();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            MarkEzdDll.SetDevCfg();
        }

    

        private void button3_Click(object sender, EventArgs e)
        {

          MarkEzdDll.ChangeTextByName(textBox2.Text, textBox3.Text);

          ShowPreviewBmp();
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }
    }

    public class MarkEzdDll
    {
             
        /// <summary>
        /// 初始化函数库
        /// PathName 是MarkEzd.dll所在的目录
        /// </summary>     
        [DllImport("MarkEzd", EntryPoint = "lmc1_Initial2", CharSet= CharSet.Unicode,CallingConvention = CallingConvention.StdCall)]
        public static extern int Initialize(string PathName,bool bTestMode);

        [DllImport("MarkEzd", EntryPoint = "lmc1_SetDevCfg", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDevCfg();
       


             
        [DllImport("MarkEzd", EntryPoint = "lmc1_Close",CharSet= CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int Close();

      
        [DllImport("MarkEzd", EntryPoint = "lmc1_LoadEzdFile", CharSet= CharSet.Unicode,CallingConvention = CallingConvention.StdCall)]
        public static extern int LoadEzdFile(string FileName);

        
        [DllImport("MarkEzd", EntryPoint = "lmc1_Mark",CharSet= CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int Mark(bool Fly);

     
      
       [DllImport("MarkEzd", EntryPoint = "lmc1_ChangeTextByName", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int ChangeTextByName(string EntName, string NewText);



        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr hObject);



        [DllImport("MarkEzd", EntryPoint = "lmc1_GetPrevBitmap2", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        internal static extern IntPtr GetCurPrevBitmap(int bmpwidth, int bmpheight);
      
       
        public static Image GetCurPreviewImage(int bmpwidth, int bmpheight)
        {
            IntPtr pBmp = GetCurPrevBitmap(bmpwidth, bmpheight);
            Image img = Image.FromHbitmap(pBmp);
            MarkEzdDll.DeleteObject(pBmp);
            return img;
        }

        



   
    }
}
