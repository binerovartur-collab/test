using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace BJJCZ_BaseDevelop
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            pictureBox1.Image = JczLmc.GetCurPreviewImage(pictureBox1.Width, pictureBox1.Height);
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            label3.Text = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss,fff");
        }

        private void label8_Click(object sender, EventArgs e)
        {

        }

        private void tabPage2_Click(object sender, EventArgs e)
        {

        }
   
        private void Form1_Load(object sender, EventArgs e)
        {
          // bool a= Login();
          //  MessageBox.Show(a.ToString());
         }

        //bool Login()
        //{
        //    char[] mes_ret = new char[1024];
        //    char[] WO = new char[64];
        //    char[] WG = new char[64];
        //    char[] WS = new char[64];
        //    char[] PN = new char[64];
        //    char[] WID = new char[64];

        //    if (!JczLmc.MES_Login(mes_ret))
        //    {
        //        return false;
        //    }
        //    else
        //    {

        //        return true;
        //    }
        //}

        //bool Check_Station()
        //{
        //    char[] mes_ret=new char[1024];
        //    bool flag = false;
        //    //连接mes，并初始化mes资源
        //    if (!JczLmc.MES_Init(mes_ret))
        //    {
        //        flag = false;
        //        goto END;
        //    }
        //    //设置需要上传到mes的固定参数
        //    if (!JczLmc.MES_LogReset())
        //    {
        //        flag = false;
        //        goto END;
        //    }
        //    //检查00179a124578 （主批号）是否在当前节点	
        //    if (!JczLmc.MES_CheckSerialNum("00179a124578", mes_ret))
        //    {
        //        flag = false;
        //        goto END;
        //    }
        //    END:
        //    //释放MES资源
        //    JczLmc.MES_Free(mes_ret);
        //    return flag;
        //}
        private void btnInitial_Click(object sender, EventArgs e)
        {
            int nErr = JczLmc.Initialize(Application.StartupPath, false);
            MessageBox.Show("Initialize-->" + nErr.ToString());
           //bool b= Check_Station();
           // MessageBox.Show(b.ToString());
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            int nErr = JczLmc.Close();
            MessageBox.Show("Close-->" + nErr.ToString());
        }

        private void btnCfg_Click(object sender, EventArgs e)
        {
            int nErr = JczLmc.SetDevCfg();
            MessageBox.Show("SetDevCfg-->" + nErr.ToString());
        }

        private void btnStopMark_Click(object sender, EventArgs e)
        {
            int nErr = JczLmc.StopMark();
            MessageBox.Show("StopMark-->" + nErr.ToString());
        }

        private void btnLoadFile_Click(object sender, EventArgs e)
        {



            //int nErr = JczLmc.lmc1_AddCircleToLib(0, 0,3, "", 0);

            //MessageBox.Show("lmc1_AddCircleToLib-->" + nErr.ToString());
            OpenFileDialog dlg = new OpenFileDialog();

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                int nErr = JczLmc.LoadEzdFile(dlg.FileName);
                if (nErr == 0)
                {
                    this.Text = dlg.FileName;
                }
                MessageBox.Show("LoadEzdFile-->" + nErr.ToString());

            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "所有文件(*xls *) | *.ezd ";
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                int nErr = JczLmc.SaveEntLibToFile(dlg.FileName);
                if (nErr == 0)
                {
                    this.Text = dlg.FileName;
                }
                MessageBox.Show("SaveEntLibToFile-->" + nErr.ToString());

            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            ushort uData = 0;
            int nErr = JczLmc.ReadPort(ref uData);
            string strData = "";
            for (int i = 0; i < 16; i++)
            {
                int nbit = 1<<i;//目标位值预置为高

                int nCurBitData = ((int)uData & nbit);//目标值与获取值做与运动，仅保留相同位数据

                bool bIsHigh = nCurBitData != 0;//数据为0，表明指定位获得值为0.
                if (bIsHigh)
                {
                    strData += "H ";
                }
                else
                {
                    strData += "L ";
                }
            }
            label1.Text = strData;
           
            MessageBox.Show("ReadPort-->" + nErr.ToString());
        }

        private void button4_Click(object sender, EventArgs e)
        {
            ushort uData = 0;
            int nErr = JczLmc.GetOutPort(ref uData);
            string strData = "";
            for (int i = 0; i < 16; i++)
            {
                int nbit = 1 << i;//目标位值预置为高

                int nCurBitData = ((int)uData & nbit);//目标值与获取值做与运动，仅保留相同位数据

                bool bIsHigh = nCurBitData != 0;//数据为0，表明指定位获得值为0.
                if (bIsHigh)
                {
                    strData += "H ";
                }
                else
                {
                    strData += "L ";
                }
            }
            label2.Text = strData;

            MessageBox.Show("GetOutPort-->" + nErr.ToString());
        }

        private void button5_Click(object sender, EventArgs e)
        {
            int nAimBitIndex = (int)numericUpDown1.Value;
            bool bAimBitState = radioButtonHigh.Checked;
            ushort uData = 0;
            int nErr1 = JczLmc.GetOutPort(ref uData);
            int nCurData = (int)uData;
            int nAimData = 1 << nAimBitIndex;
            if (bAimBitState)
            {
                nAimData |= nCurData;
            }
            else
            {
                nAimData &= ~nCurData;
            }

            int nErr2 = JczLmc.WritePort((ushort)nAimData);
            MessageBox.Show("WritePort-->" + nErr2.ToString());
        }

        private void button7_Click(object sender, EventArgs e)
        {
            string strEntName = textBox1.Text;
            string strEntData = TB.Text;
            int nErr = JczLmc.ChangeTextByName(strEntName, strEntData);
          
            MessageBox.Show("ChangeTextByName-->" + nErr.ToString());
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            int nErr = JczLmc.Mark(false);

            MessageBox.Show("Mark-->" + nErr.ToString());
        }

        private void button6_Click(object sender, EventArgs e)
        {
            if (!backgroundWorker1.IsBusy)
            {
                backgroundWorker1.RunWorkerAsync();
            }
            else
            {
                MessageBox.Show("后台线程工作中");
            }
        }

        private void button8_Click(object sender, EventArgs e)
        {
            ushort  uCount = JczLmc.GetEntityCount();
            label6.Text = "Total Ent Count-->" + uCount.ToString();
        }

        private void button9_Click(object sender, EventArgs e)
        {
            int nAimEntIndex = (int)numericUpDownEntIndex.Value;
            string strEntName= JczLmc.GetEntityNameByIndex(nAimEntIndex);
            label7.Text = strEntName;
        }

        private void button10_Click(object sender, EventArgs e)
        {
            int nAimEntIndex = (int)numericUpDownEntIndex.Value;
            string strNewEntName = textBox3.Text;
            int nErr = JczLmc.SetEntityNameByIndex(nAimEntIndex, strNewEntName);
            MessageBox.Show("SetEntityNameByIndex-->" + nErr.ToString());
            
        }

        private void button11_Click(object sender, EventArgs e)
        {
            string strCurEntName = textBoxEntName.Text;
            string strNewEntName = textBox3.Text;
            int nErr = JczLmc.ChangeEntName(strCurEntName, strNewEntName);
            MessageBox.Show("ChangeEntName-->" + nErr.ToString());
        }

        private void button13_Click(object sender, EventArgs e)
        {
            string strCurEntName = textBoxEntName.Text;
            string strNewEntName = textBox5.Text;
            int nErr = JczLmc.CopyEnt(strCurEntName, strNewEntName);
            MessageBox.Show("CopyEnt-->" + nErr.ToString());
        }

        private void button14_Click(object sender, EventArgs e)
        {
            string strCurEntName = textBoxEntName.Text;
          
            int nErr = JczLmc.DeleteEnt(strCurEntName);
            MessageBox.Show("DeleteEnt-->" + nErr.ToString());
        }

        private void button12_Click(object sender, EventArgs e)
        {
            string strCurEntName = textBoxEntName.Text;
            double dx1 = 0, dx2 = 0, dy1 = 0, dy2 = 0, dz = 0;
            int nErr = JczLmc.GetEntSize(strCurEntName,ref dx1,ref dy1,ref dx2,ref dy2,ref dz);
            textBox17.Text = "MinX:"+dx1.ToString("0.000") + ":" + "MaxX:" + dx2.ToString("0.000") + ":" +"MinY:"+ dy1.ToString("0.000") + ":" + "MaxY:" + dy2.ToString("0.000") + ":" + "Z:" + dz.ToString("0.000");
            MessageBox.Show("GetEntSize-->" + nErr.ToString());
        }

        private void button16_Click(object sender, EventArgs e)
        {
            string strCurEntName = textBoxEntName.Text;
            double dRotateCenX = double.Parse(textBox6.Text);
            double dRotateCenY = double.Parse(textBox7.Text);
            double dRotateAngle = double.Parse(textBox8.Text)/180*Math.PI;
            int nErr = JczLmc.RotateEnt(strCurEntName,dRotateCenX,dRotateCenY,dRotateAngle);
            MessageBox.Show("RotateEnt-->" + nErr.ToString());
        }

        private void button15_Click(object sender, EventArgs e)
        {
            string strCurEntName = textBoxEntName.Text;
            double dMoveDisX = double.Parse(textBox14.Text);
            double dMoveDisY = double.Parse(textBox13.Text);           
            int nErr = JczLmc.MoveEnt(strCurEntName, dMoveDisX, dMoveDisY);
            MessageBox.Show("MoveEnt-->" + nErr.ToString());
        }

        private void button17_Click(object sender, EventArgs e)
        {
            string strCurEntName = textBoxEntName.Text;
            double dScaleCenX = double.Parse(textBox11.Text);
            double dScaleCenY = double.Parse(textBox10.Text);
            double dScalX = double.Parse(textBox12.Text);
            double dScalY = double.Parse(textBox9.Text);
            int nErr = JczLmc.ScaleEnt(strCurEntName, dScaleCenX, dScaleCenY, dScalX, dScalY);
            MessageBox.Show("ScaleEnt-->" + nErr.ToString());
        }

        private void button18_Click(object sender, EventArgs e)
        {
            int nErr = JczLmc.LaserOn(checkBoxLaserOn.Checked);
            MessageBox.Show("LaserOn-->" + nErr.ToString());
        }

        private void button19_Click(object sender, EventArgs e)
        {
         
            double dMoveAimX = double.Parse(textBox16.Text);
            double dMoveAimY = double.Parse(textBox18.Text);
            int nErr = JczLmc.GotoPos(dMoveAimX, dMoveAimY);
            MessageBox.Show("GotoPos-->" + nErr.ToString());
        }

        private void button20_Click(object sender, EventArgs e)
        {
            double dCurX = 0, dCurY = 0;
            int nErr = JczLmc.GetCurCoor(ref dCurX,ref dCurY);
            label25.Text = dCurX.ToString("0.0000") + ":" + dCurY.ToString("0.0000");
            MessageBox.Show("GetCurCoor-->" + nErr.ToString());
        }

        private void button23_Click(object sender, EventArgs e)
        {
            double dFlySpeed = 0;
            int nErr = JczLmc.GetFlySpeed(ref dFlySpeed);
            label27.Text = dFlySpeed.ToString("0.0000") ;
            MessageBox.Show("GetFlySpeed-->" + nErr.ToString());
        }

        private void button27_Click(object sender, EventArgs e)
        {
            double dPointX = double.Parse(textBox19.Text);
            double dPointY = double.Parse(textBox15.Text);
            int nPointDelayMs = int.Parse(textBox21.Text);
            int nPointPen = int.Parse(textBox20.Text);
          

             int nErr = JczLmc.MarkPoint(dPointX,dPointY,nPointDelayMs,nPointPen);
            MessageBox.Show("lmc1_AddCircleToLib-->" + nErr.ToString());
        }

        private void button21_Click(object sender, EventArgs e)
        {
            string strRunEntName = textBoxRunEntName.Text;
            int nErr = JczLmc.MarkEntity(strRunEntName);
            MessageBox.Show("MarkEntity-->" + nErr.ToString());
        }

        private void button22_Click(object sender, EventArgs e)
        {
            string strRunEntName = textBoxRunEntName.Text;
            int nErr = JczLmc.RedLightMarkByEnt(strRunEntName,checkBoxContour.Checked);
            MessageBox.Show("RedLightMarkByEnt-->" + nErr.ToString());
        }

        private void button26_Click(object sender, EventArgs e)
        {
            string strRunEntName = textBoxRunEntName.Text;
            int nErr = JczLmc.MarkEntityFly(strRunEntName);
            MessageBox.Show("MarkEntityFly-->" + nErr.ToString());
        }

        private void button24_Click(object sender, EventArgs e)
        {
            int nErr = JczLmc.RedMark();
            MessageBox.Show("RedMark-->" + nErr.ToString());
        }

        private void button30_Click(object sender, EventArgs e)
        {
            int nErr = JczLmc.RedMarkContour();
            MessageBox.Show("RedMarkContour-->" + nErr.ToString());
        }

        private void button25_Click(object sender, EventArgs e)
        {
            int nErr = JczLmc.MarkFlyByStartSignal();
            MessageBox.Show("MarkFlyByStartSignal-->" + nErr.ToString());
        }

        private void button28_Click(object sender, EventArgs e)
        {
            double dStartPointX = double.Parse(textBox25.Text);
            double dStartPointY = double.Parse(textBox24.Text);
            double dEndPointX = double.Parse(textBox26.Text);
            double dEndPointY = double.Parse(textBox23.Text);
         
            int nPointPen = int.Parse(textBox22.Text);
            int nErr = JczLmc.MarkLine(dStartPointX,dStartPointY,dEndPointX,dEndPointY, nPointPen);
            MessageBox.Show("MarkLine-->" + nErr.ToString());
        }

        private void button32_Click(object sender, EventArgs e)
        {
            bool bAimIsLowToHigh = checkBoxLowToHigh.Checked; ;
            int nErr = JczLmc.EnableLockInputPort(bAimIsLowToHigh);
            MessageBox.Show("EnableLockInputPort-->" + nErr.ToString());
        }

        private void button29_Click(object sender, EventArgs e)
        {
            ushort uData=0 ;
            int nErr = JczLmc.ReadLockPort(ref uData);
            string strData = "";
            for (int i = 0; i < 16; i++)
            {
                int nbit = 1 << i;//目标位值预置为高

                int nCurBitData = ((int)uData & nbit);//目标值与获取值做与运动，仅保留相同位数据

                bool bIsHigh = nCurBitData != 0;//数据为0，表明指定位获得值为0.
                if (bIsHigh)
                {
                    strData += "H ";
                }
                else
                {
                    strData += "L ";
                }
            }
            label36.Text = strData;
            MessageBox.Show("ReadLockPort-->" + nErr.ToString());
        }

        private void button31_Click(object sender, EventArgs e)
        {
          
            int nErr = JczLmc.ClearLockInputPort();
            MessageBox.Show("ClearLockInputPort-->" + nErr.ToString());
        }

        private void label15_Click(object sender, EventArgs e)
        {

        }

        private void label16_Click(object sender, EventArgs e)
        {

        }
    }
}
