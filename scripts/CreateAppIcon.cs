using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Collections.Generic;
class CreateAppIcon {
    static GraphicsPath Round(RectangleF r,float radius){var p=new GraphicsPath();float d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    static Bitmap Render(int size){
        var b=new Bitmap(size,size);using(var g=Graphics.FromImage(b)){
            g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);g.ScaleTransform(size/256f,size/256f);
            using(var tile=Round(new RectangleF(8,8,240,240),52))using(var fill=new LinearGradientBrush(new Point(20,12),new Point(230,246),Color.FromArgb(39,60,86),Color.FromArgb(13,24,39)))g.FillPath(fill,tile);
            using(var edge=Round(new RectangleF(9,9,238,238),51))using(var pen=new Pen(Color.FromArgb(75,101,126),2))g.DrawPath(pen,edge);
            using(var glow=new SolidBrush(Color.FromArgb(24,112,231,196)))g.FillEllipse(glow,28,68,182,154);
            using(var board=Round(new RectangleF(31,122,134,79),13))using(var fill=new SolidBrush(Color.FromArgb(170,247,218)))g.FillPath(fill,board);
            using(var ink=new SolidBrush(Color.FromArgb(22,64,67))){for(int row=0;row<3;row++)for(int col=0;col<7;col++)using(var key=Round(new RectangleF(43+col*16,135+row*15,10,8),2))g.FillPath(ink,key);using(var space=Round(new RectangleF(69,182,60,7),2))g.FillPath(ink,space);}
            using(var mouse=Round(new RectangleF(180,99,45,100),22))using(var fill=new SolidBrush(Color.FromArgb(155,209,255)))g.FillPath(fill,mouse);
            using(var line=new Pen(Color.FromArgb(28,66,101),3)){g.DrawLine(line,202.5f,101,202.5f,133);g.DrawLine(line,183,135,222,135);}
            using(var wheel=Round(new RectangleF(199,112,7,14),3))using(var fill=new SolidBrush(Color.FromArgb(24,69,100)))g.FillPath(fill,wheel);
            using(var battery=Round(new RectangleF(48,74,72,29),7))using(var pen=new Pen(Color.FromArgb(156,245,211),5))g.DrawPath(pen,battery);
            using(var fill=new SolidBrush(Color.FromArgb(156,245,211))){g.FillRectangle(fill,122,83,5,11);using(var charge=Round(new RectangleF(55,81,50,15),3))g.FillPath(fill,charge);}
            using(var signal=new Pen(Color.FromArgb(155,209,255),5)){signal.StartCap=LineCap.Round;signal.EndCap=LineCap.Round;g.DrawArc(signal,149,43,70,70,217,105);g.DrawArc(signal,162,58,44,44,217,105);}
        }return b;
    }
    static void Main(string[] args){int[] sizes={16,20,24,32,48,64,128,256};var frames=new List<byte[]>();foreach(int size in sizes)using(var b=Render(size))using(var m=new MemoryStream()){b.Save(m,ImageFormat.Png);frames.Add(m.ToArray());if(size==256)b.Save(args[1],ImageFormat.Png);}
        using(var f=File.Create(args[0]))using(var w=new BinaryWriter(f)){w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(frames[i].Length);w.Write(offset);offset+=frames[i].Length;}foreach(var bytes in frames)w.Write(bytes);}
    }
}
