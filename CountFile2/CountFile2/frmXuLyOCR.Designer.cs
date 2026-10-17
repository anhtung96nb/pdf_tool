
using System.Drawing;
using System.Windows.Forms;

namespace CountFile2
{
    partial class frmXuLyOCR
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.label1 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.txtFolderOut = new System.Windows.Forms.TextBox();
            this.btExportExcel = new System.Windows.Forms.Button();
            this.btOCR = new System.Windows.Forms.Button();
            this.label4 = new System.Windows.Forms.Label();
            this.btOpen = new System.Windows.Forms.Button();
            this.txtFolderIn = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.lbCurrentFolder = new System.Windows.Forms.Label();
            this.paneForm = new System.Windows.Forms.Panel();
            this.btnUpdateOCR = new System.Windows.Forms.Button();
            this.lbLoaiVB = new System.Windows.Forms.Label();
            this.lbTrichYeu = new System.Windows.Forms.Label();
            this.lbNgayBH = new System.Windows.Forms.Label();
            this.lbCoQuanBH = new System.Windows.Forms.Label();
            this.lbSoVB = new System.Windows.Forms.Label();
            this.txbTrichYeu = new System.Windows.Forms.TextBox();
            this.txbLoaiVB = new System.Windows.Forms.TextBox();
            this.txbNgayBH = new System.Windows.Forms.TextBox();
            this.txbCoQuanBH = new System.Windows.Forms.TextBox();
            this.txbSoVB = new System.Windows.Forms.TextBox();
            this.panel3 = new System.Windows.Forms.Panel();
            this.treeViewFolders = new System.Windows.Forms.TreeView();
            this.listViewFiles = new System.Windows.Forms.ListView();
            this.txtOcrMsg = new System.Windows.Forms.Label();
            this.panel2 = new System.Windows.Forms.Panel();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.btnOCRAll = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            this.panel1.SuspendLayout();
            this.paneForm.SuspendLayout();
            this.panel3.SuspendLayout();
            this.panel2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Arial", 15F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(303, 7);
            this.label1.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(170, 24);
            this.label1.TabIndex = 6;
            this.label1.Text = "XỬ LÝ FILE PDF ";
            // 
            // button1
            // 
            this.button1.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button1.Location = new System.Drawing.Point(616, 40);
            this.button1.Margin = new System.Windows.Forms.Padding(2);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(96, 28);
            this.button1.TabIndex = 26;
            this.button1.Text = "Xuất Exel";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // txtFolderOut
            // 
            this.txtFolderOut.Location = new System.Drawing.Point(143, 36);
            this.txtFolderOut.Margin = new System.Windows.Forms.Padding(2);
            this.txtFolderOut.Name = "txtFolderOut";
            this.txtFolderOut.Size = new System.Drawing.Size(421, 21);
            this.txtFolderOut.TabIndex = 24;
            this.txtFolderOut.TextChanged += new System.EventHandler(this.txtFolderOut_TextChanged);
            // 
            // btExportExcel
            // 
            this.btExportExcel.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btExportExcel.Location = new System.Drawing.Point(737, 40);
            this.btExportExcel.Margin = new System.Windows.Forms.Padding(2);
            this.btExportExcel.Name = "btExportExcel";
            this.btExportExcel.Size = new System.Drawing.Size(96, 28);
            this.btExportExcel.TabIndex = 20;
            this.btExportExcel.Text = "Xuất file lỗi";
            this.btExportExcel.UseVisualStyleBackColor = true;
            // 
            // btOCR
            // 
            this.btOCR.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btOCR.Location = new System.Drawing.Point(616, 9);
            this.btOCR.Margin = new System.Windows.Forms.Padding(2);
            this.btOCR.Name = "btOCR";
            this.btOCR.Size = new System.Drawing.Size(96, 29);
            this.btOCR.TabIndex = 17;
            this.btOCR.Text = "Orc File";
            this.btOCR.UseVisualStyleBackColor = true;
            this.btOCR.Click += new System.EventHandler(this.btOCR_Click);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.Location = new System.Drawing.Point(21, 40);
            this.label4.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(84, 15);
            this.label4.TabIndex = 4;
            this.label4.Text = "Thư mục Out:";
            // 
            // btOpen
            // 
            this.btOpen.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btOpen.Location = new System.Drawing.Point(568, 8);
            this.btOpen.Margin = new System.Windows.Forms.Padding(2);
            this.btOpen.Name = "btOpen";
            this.btOpen.Size = new System.Drawing.Size(26, 21);
            this.btOpen.TabIndex = 3;
            this.btOpen.Text = "....";
            this.btOpen.UseVisualStyleBackColor = true;
            this.btOpen.Visible = false;
            this.btOpen.Click += new System.EventHandler(this.btOpen_Click);
            // 
            // txtFolderIn
            // 
            this.txtFolderIn.Location = new System.Drawing.Point(143, 8);
            this.txtFolderIn.Margin = new System.Windows.Forms.Padding(2);
            this.txtFolderIn.Name = "txtFolderIn";
            this.txtFolderIn.Size = new System.Drawing.Size(421, 21);
            this.txtFolderIn.TabIndex = 2;
            this.txtFolderIn.Visible = false;
            this.txtFolderIn.TextChanged += new System.EventHandler(this.txtFolderIn_TextChanged);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Arial", 7.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.ForeColor = System.Drawing.Color.Red;
            this.label3.Location = new System.Drawing.Point(120, 9);
            this.label3.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(19, 14);
            this.label3.TabIndex = 1;
            this.label3.Text = "(*)";
            this.label3.Visible = false;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(22, 9);
            this.label2.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(99, 15);
            this.label2.TabIndex = 0;
            this.label2.Text = "Thư mục nguồn:";
            this.label2.Visible = false;
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Location = new System.Drawing.Point(14, 417);
            this.lblStatus.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(0, 13);
            this.lblStatus.TabIndex = 8;
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.lbCurrentFolder);
            this.panel1.Controls.Add(this.paneForm);
            this.panel1.Controls.Add(this.panel3);
            this.panel1.Controls.Add(this.txtOcrMsg);
            this.panel1.Controls.Add(this.panel2);
            this.panel1.Controls.Add(this.btnOCRAll);
            this.panel1.Controls.Add(this.button2);
            this.panel1.Controls.Add(this.button1);
            this.panel1.Controls.Add(this.txtFolderOut);
            this.panel1.Controls.Add(this.btExportExcel);
            this.panel1.Controls.Add(this.btOCR);
            this.panel1.Controls.Add(this.label4);
            this.panel1.Controls.Add(this.btOpen);
            this.panel1.Controls.Add(this.txtFolderIn);
            this.panel1.Controls.Add(this.label3);
            this.panel1.Controls.Add(this.label2);
            this.panel1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.panel1.Location = new System.Drawing.Point(13, 32);
            this.panel1.Margin = new System.Windows.Forms.Padding(2);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(1218, 508);
            this.panel1.TabIndex = 7;
            this.panel1.Paint += new System.Windows.Forms.PaintEventHandler(this.panel1_Paint);
            // 
            // lbCurrentFolder
            // 
            this.lbCurrentFolder.AutoSize = true;
            this.lbCurrentFolder.Location = new System.Drawing.Point(7, 74);
            this.lbCurrentFolder.Name = "lbCurrentFolder";
            this.lbCurrentFolder.Size = new System.Drawing.Size(81, 15);
            this.lbCurrentFolder.TabIndex = 37;
            this.lbCurrentFolder.Text = "Curent Folder";
            // 
            // paneForm
            // 
            this.paneForm.Controls.Add(this.btnUpdateOCR);
            this.paneForm.Controls.Add(this.lbLoaiVB);
            this.paneForm.Controls.Add(this.lbTrichYeu);
            this.paneForm.Controls.Add(this.lbNgayBH);
            this.paneForm.Controls.Add(this.lbCoQuanBH);
            this.paneForm.Controls.Add(this.lbSoVB);
            this.paneForm.Controls.Add(this.txbTrichYeu);
            this.paneForm.Controls.Add(this.txbLoaiVB);
            this.paneForm.Controls.Add(this.txbNgayBH);
            this.paneForm.Controls.Add(this.txbCoQuanBH);
            this.paneForm.Controls.Add(this.txbSoVB);
            this.paneForm.Location = new System.Drawing.Point(984, 96);
            this.paneForm.Name = "paneForm";
            this.paneForm.Size = new System.Drawing.Size(231, 409);
            this.paneForm.TabIndex = 36;
            // 
            // btnUpdateOCR
            // 
            this.btnUpdateOCR.Location = new System.Drawing.Point(14, 298);
            this.btnUpdateOCR.Name = "btnUpdateOCR";
            this.btnUpdateOCR.Size = new System.Drawing.Size(75, 23);
            this.btnUpdateOCR.TabIndex = 10;
            this.btnUpdateOCR.Text = "Update";
            this.btnUpdateOCR.UseVisualStyleBackColor = true;
            this.btnUpdateOCR.Click += new System.EventHandler(this.btnUpdateOCR_Click);
            // 
            // lbLoaiVB
            // 
            this.lbLoaiVB.AutoSize = true;
            this.lbLoaiVB.Location = new System.Drawing.Point(11, 180);
            this.lbLoaiVB.Name = "lbLoaiVB";
            this.lbLoaiVB.Size = new System.Drawing.Size(80, 15);
            this.lbLoaiVB.TabIndex = 9;
            this.lbLoaiVB.Text = "Loại Văn Bản";
            // 
            // lbTrichYeu
            // 
            this.lbTrichYeu.AutoSize = true;
            this.lbTrichYeu.Location = new System.Drawing.Point(11, 237);
            this.lbTrichYeu.Name = "lbTrichYeu";
            this.lbTrichYeu.Size = new System.Drawing.Size(58, 15);
            this.lbTrichYeu.TabIndex = 8;
            this.lbTrichYeu.Text = "Trích Yếu";
            // 
            // lbNgayBH
            // 
            this.lbNgayBH.AutoSize = true;
            this.lbNgayBH.Location = new System.Drawing.Point(11, 127);
            this.lbNgayBH.Name = "lbNgayBH";
            this.lbNgayBH.Size = new System.Drawing.Size(93, 15);
            this.lbNgayBH.TabIndex = 7;
            this.lbNgayBH.Text = "Ngày Ban Hành";
            // 
            // lbCoQuanBH
            // 
            this.lbCoQuanBH.AutoSize = true;
            this.lbCoQuanBH.Location = new System.Drawing.Point(11, 67);
            this.lbCoQuanBH.Name = "lbCoQuanBH";
            this.lbCoQuanBH.Size = new System.Drawing.Size(113, 15);
            this.lbCoQuanBH.TabIndex = 6;
            this.lbCoQuanBH.Text = "Cơ Quan Ban Hành";
            // 
            // lbSoVB
            // 
            this.lbSoVB.AutoSize = true;
            this.lbSoVB.Location = new System.Drawing.Point(11, 18);
            this.lbSoVB.Name = "lbSoVB";
            this.lbSoVB.Size = new System.Drawing.Size(71, 15);
            this.lbSoVB.TabIndex = 5;
            this.lbSoVB.Text = "Số Văn Bản";
            // 
            // txbTrichYeu
            // 
            this.txbTrichYeu.Location = new System.Drawing.Point(14, 255);
            this.txbTrichYeu.Name = "txbTrichYeu";
            this.txbTrichYeu.Size = new System.Drawing.Size(202, 21);
            this.txbTrichYeu.TabIndex = 4;
            // 
            // txbLoaiVB
            // 
            this.txbLoaiVB.Location = new System.Drawing.Point(14, 198);
            this.txbLoaiVB.Name = "txbLoaiVB";
            this.txbLoaiVB.Size = new System.Drawing.Size(202, 21);
            this.txbLoaiVB.TabIndex = 3;
            // 
            // txbNgayBH
            // 
            this.txbNgayBH.Location = new System.Drawing.Point(14, 145);
            this.txbNgayBH.Name = "txbNgayBH";
            this.txbNgayBH.Size = new System.Drawing.Size(202, 21);
            this.txbNgayBH.TabIndex = 2;
            // 
            // txbCoQuanBH
            // 
            this.txbCoQuanBH.Location = new System.Drawing.Point(14, 86);
            this.txbCoQuanBH.Name = "txbCoQuanBH";
            this.txbCoQuanBH.Size = new System.Drawing.Size(202, 21);
            this.txbCoQuanBH.TabIndex = 1;
            // 
            // txbSoVB
            // 
            this.txbSoVB.Location = new System.Drawing.Point(14, 36);
            this.txbSoVB.Name = "txbSoVB";
            this.txbSoVB.Size = new System.Drawing.Size(202, 21);
            this.txbSoVB.TabIndex = 0;
            // 
            // panel3
            // 
            this.panel3.Controls.Add(this.treeViewFolders);
            this.panel3.Controls.Add(this.listViewFiles);
            this.panel3.Location = new System.Drawing.Point(4, 93);
            this.panel3.Name = "panel3";
            this.panel3.Size = new System.Drawing.Size(322, 409);
            this.panel3.TabIndex = 35;
            // 
            // treeViewFolders
            // 
            this.treeViewFolders.Location = new System.Drawing.Point(3, 3);
            this.treeViewFolders.Name = "treeViewFolders";
            this.treeViewFolders.Size = new System.Drawing.Size(316, 195);
            this.treeViewFolders.TabIndex = 2;
            // 
            // listViewFiles
            // 
            this.listViewFiles.HideSelection = false;
            this.listViewFiles.Location = new System.Drawing.Point(3, 201);
            this.listViewFiles.Name = "listViewFiles";
            this.listViewFiles.Size = new System.Drawing.Size(316, 199);
            this.listViewFiles.TabIndex = 1;
            this.listViewFiles.UseCompatibleStateImageBehavior = false;
            this.listViewFiles.SelectedIndexChanged += new System.EventHandler(this.listViewFilePdf_SelectedIndexChanged);
            // 
            // txtOcrMsg
            // 
            this.txtOcrMsg.AutoSize = true;
            this.txtOcrMsg.ForeColor = System.Drawing.SystemColors.MenuHighlight;
            this.txtOcrMsg.Location = new System.Drawing.Point(985, 74);
            this.txtOcrMsg.Name = "txtOcrMsg";
            this.txtOcrMsg.Size = new System.Drawing.Size(103, 15);
            this.txtOcrMsg.TabIndex = 32;
            this.txtOcrMsg.Text = "OCR Successfully";
            // 
            // panel2
            // 
            this.panel2.AutoScroll = true;
            this.panel2.Controls.Add(this.pictureBox1);
            this.panel2.Location = new System.Drawing.Point(332, 96);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(646, 409);
            this.panel2.TabIndex = 31;
            // 
            // pictureBox1
            // 
            this.pictureBox1.Location = new System.Drawing.Point(0, 0);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(640, 400);
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            this.pictureBox1.Paint += new System.Windows.Forms.PaintEventHandler(this.pictureBox1_Paint);
            this.pictureBox1.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseDown);
            this.pictureBox1.MouseMove += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseMove);
            this.pictureBox1.MouseUp += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseUp);
            this.pictureBox1.PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.pictureBox1_PreviewKeyDown);
            // 
            // btnOCRAll
            // 
            this.btnOCRAll.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnOCRAll.Location = new System.Drawing.Point(737, 9);
            this.btnOCRAll.Margin = new System.Windows.Forms.Padding(2);
            this.btnOCRAll.Name = "btnOCRAll";
            this.btnOCRAll.Size = new System.Drawing.Size(96, 28);
            this.btnOCRAll.TabIndex = 28;
            this.btnOCRAll.Text = "OCR Folder";
            this.btnOCRAll.UseVisualStyleBackColor = true;
            this.btnOCRAll.Click += new System.EventHandler(this.btPDF2Lop_Click);
            // 
            // button2
            // 
            this.button2.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button2.Location = new System.Drawing.Point(568, 36);
            this.button2.Margin = new System.Windows.Forms.Padding(2);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(26, 21);
            this.button2.TabIndex = 27;
            this.button2.Text = "....";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // frmXuLyOCR
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1271, 557);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.panel1);
            this.Margin = new System.Windows.Forms.Padding(2);
            this.Name = "frmXuLyOCR";
            this.Text = "frmXuLyOCR";
            this.Load += new System.EventHandler(this.frmXuLyOCR_Load);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.paneForm.ResumeLayout(false);
            this.paneForm.PerformLayout();
            this.panel3.ResumeLayout(false);
            this.panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.TextBox txtFolderOut;
        private System.Windows.Forms.Button btExportExcel;
        private System.Windows.Forms.Button btOCR;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button btOpen;
        private System.Windows.Forms.TextBox txtFolderIn;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button btnOCRAll;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.PictureBox pictureBox1;
        private Panel paneForm;
        private Panel panel3;
        private Label txtOcrMsg;
        private ListView listViewFiles;
        private Label lbCoQuanBH;
        private Label lbSoVB;
        private TextBox txbTrichYeu;
        private TextBox txbLoaiVB;
        private TextBox txbNgayBH;
        private TextBox txbCoQuanBH;
        private TextBox txbSoVB;
        private Button btnUpdateOCR;
        private Label lbLoaiVB;
        private Label lbTrichYeu;
        private Label lbNgayBH;
        private Label lbCurrentFolder;
        private TreeView treeViewFolders;
    }
}