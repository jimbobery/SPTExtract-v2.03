using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.IO;
using System.Security.Cryptography;
using static SPTExtract.Constants.XeroUrls;
using Xero.NetStandard.OAuth2.Client;
using Xero.NetStandard.OAuth2.Token;
using Xero.NetStandard.OAuth2.Config;
using Xero.NetStandard.OAuth2.Model.Accounting;
using Xero.NetStandard.OAuth2.Api;

namespace SPTExtract
{
    public partial class frmSPTExract : Form
    {
        public frmSPTExract()
        {
            InitializeComponent();
            RedirectUri = "http://localhost:8888/callback";
            XeroConfiguration xeroConfigTemp = new XeroConfiguration();
            xeroConfigTemp.AppName = "SPTExtract";
            xeroConfigTemp.ClientId = "56A805C0E65E443ABEC86717DE2A9087";
            xeroConfigTemp.Scope = Uri.EscapeUriString("offline_access openid profile email accounting.transactions accounting.settings accounting.contacts");
            xeroConfigTemp.State = "12345678";
            XeroConfig = xeroConfigTemp;
        }

        private XeroConfiguration _xeroConfig;

        public XeroConfiguration XeroConfig
        {
            get => _xeroConfig;

            set
            {
                _xeroConfig = value;
            }
        }

        private string _redirectUri;
        public string RedirectUri
        {
            get => _redirectUri;

            set
            {
                _redirectUri = value;
            }
        }

        private string _state;
        public string State
        {
            get => _state;

            set
            {
                _state = value;
            }
        }

        private string _codeVerifier;
        public string CodeVerifier
        {
            get => _codeVerifier;

            set
            {
                _codeVerifier = value;
            }
        }

        private string _authorisationCode;
        public string AuthorisationCode
        {
            get => _authorisationCode;

            set
            {
                _authorisationCode = value;
            }
        }

        private string _tenantId;
        public string TenantId
        {
            get => _tenantId;

            set
            {
                _tenantId = value;
            }
        }

        private string _tenantName;
        public string TenantName
        {
            get => _tenantName;

            set
            {
                lblConnectedOrgName.Visible = true;
                lblConnectedOrgName.Text = "Connected to: " + value;
                pnlXeroConnection.BackColor = Color.SpringGreen;
                _tenantName = value;
            }
        }
        private static string GenerateCodeVerifier()
        {
            //Generate a random string for our code verifier
            var rng = RandomNumberGenerator.Create();
            var bytes = new byte[32];
            rng.GetBytes(bytes);

            var codeVerifier = Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            return codeVerifier;
        }

        private async void BtnConnect_Click(object sender, EventArgs e)
        {
            CodeVerifier = GenerateCodeVerifier();

            //construct the link that the end user will need to visit in order to authorize the app

            //generate the code challenge based on the verifier
            string codeChallenge;
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var challengeBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(CodeVerifier));
                codeChallenge = Convert.ToBase64String(challengeBytes)
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
            }

            var authLink = $"{AuthorisationUrl}?response_type=code&client_id={XeroConfig.ClientId}&redirect_uri={RedirectUri}&scope={XeroConfig.Scope}&state={XeroConfig.State}&code_challenge={codeChallenge}&code_challenge_method=S256";

            //open web browser with the link generated
            System.Diagnostics.Process.Start(authLink);

            //start webserver to listen for the callback
            await LocalHttpListener.StartWebServer(this);

            LoadCustomers();
        }

        private async void btnGetInvoices_Click(object sender, EventArgs e)
        {
            panelResults.Controls.Clear();
            Label lblInvNum = new Label();
            lblInvNum.Text = "Invoice Num";
            lblInvNum.Location = new Point(20, 10);
            lblInvNum.Size = new Size(67, 13);
            panelResults.Controls.Add(lblInvNum);
            Label lblLineNum = new Label();
            lblLineNum.Text = "Line Num";
            lblLineNum.Location = new Point(105, 10);
            lblLineNum.Size = new Size(60, 13);
            panelResults.Controls.Add(lblLineNum);
            Label lbJobNum = new Label();
            lbJobNum.Text = "Job Num";
            lbJobNum.Location = new Point(195, 10);
            panelResults.Controls.Add(lbJobNum);
            Label lblQuantity = new Label();
            lblQuantity.Text = "Quantity";
            lblQuantity.Location = new Point(300, 10);
            panelResults.Controls.Add(lblQuantity);
            Label lblUnitPrice = new Label();
            lblUnitPrice.Text = "Unit Price";
            lblUnitPrice.Location = new Point(405, 10);
            panelResults.Controls.Add(lblUnitPrice);
            Label lblSupplierNum = new Label();
            lblSupplierNum.Text = "Supplier Num";
            lblSupplierNum.Location = new Point(510, 10);
            panelResults.Controls.Add(lblSupplierNum);
            Label lblBuyerNum = new Label();
            lblBuyerNum.Text = "Buyer Num";
            lblBuyerNum.Location = new Point(615, 10);
            panelResults.Controls.Add(lblBuyerNum);
            Label lblDueDate = new Label();
            lblDueDate.Text = "Due Date";
            lblDueDate.Location = new Point(720, 10);
            panelResults.Controls.Add(lblDueDate);

            // check token
            XeroOAuth2Token xeroToken = null;
            try
            {
                xeroToken = TokenUtilities.GetStoredToken();
                if (xeroToken == null) throw new Exception("No Xero token available");

                var utcTimeNow = DateTime.UtcNow;

                if (utcTimeNow > xeroToken.ExpiresAtUtc)
                {
                    var client = new XeroClient(XeroConfig);
                    xeroToken = (XeroOAuth2Token)await client.RefreshAccessTokenAsync(xeroToken);
                    TokenUtilities.StoreToken(xeroToken);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "btnGetInvoices - Token handling failed");
                MessageBox.Show("Failed to retrieve or refresh Xero token. See log for details.", "SPTExtract", MessageBoxButtons.OK);
                return;
            }

            var AccountingApi = new AccountingApi();

            // get invoices
            DateTime fromDate = dteFrom.Value;
            DateTime toDate = dteTo.Value;
            List<Guid> contact = new List<Guid>();
            contact.Add(new Guid(comboBoxCustomer.SelectedValue.ToString()));
            String where = @"Type==""ACCREC"" && Status==""DRAFT"" && Date >= DateTime(" + fromDate.Year.ToString() + ", " + fromDate.Month.ToString() + ", " + fromDate.Day.ToString() + ") && Date <= DateTime(" + toDate.Year.ToString() + ", " + toDate.Month.ToString() + ", " + toDate.Day.ToString() + ")";

            List<Invoice> invoices = new List<Invoice>();
            List<Invoice> invoicesPaged = new List<Invoice>();
            int page = 1;
            do
            {
                var response = await AccountingApi.GetInvoicesAsync(xeroToken.AccessToken, TokenUtilities.GetCurrentTenantId().ToString(), null, where, null, null, null, contact, null, page);
                invoicesPaged = response._Invoices;
                invoices.AddRange(invoicesPaged);
                page++;
            } while (invoicesPaged.Count() > 0);

            if (invoices.Count == 0)
            {
                MessageBox.Show("No invoices found", "SPTExtract", MessageBoxButtons.OK);
                return;
            }

            // loop through invoices
            int loopCount = 0;
            foreach (Invoice invoice in invoices)
            {
                // loop the line items
                int lineCount = 1;
                foreach (LineItem lineItem in invoice.LineItems)
                {
                    // y position
                    int yvar = 10 + (21 * (loopCount + 1));

                    TextBox textBoxId = new TextBox();
                    textBoxId.Name = "txtId" + loopCount.ToString();
                    textBoxId.Text = invoice.InvoiceID.ToString();
                    textBoxId.Enabled = false;
                    textBoxId.Visible = false;
                    textBoxId.Location = new Point(0, yvar);
                    panelResults.Controls.Add(textBoxId);

                    CheckBox checkLine = new CheckBox();
                    checkLine.Name = "chkLine" + loopCount.ToString();
                    checkLine.Text = "";
                    checkLine.Width = 15;
                    checkLine.Checked = true;
                    checkLine.Location = new Point(0, yvar);
                    panelResults.Controls.Add(checkLine);

                    TextBox textBoxInvNum = new TextBox();
                    textBoxInvNum.Name = "txtInvNum" + loopCount.ToString();
                    textBoxInvNum.Text = invoice.InvoiceNumber;
                    textBoxInvNum.Enabled = false;
                    textBoxInvNum.BackColor = Color.White;
                    textBoxInvNum.Width = 80;
                    textBoxInvNum.Location = new Point(20, yvar);
                    panelResults.Controls.Add(textBoxInvNum);

                    TextBox textBoxLineNum = new TextBox();
                    textBoxLineNum.Name = "txtLineNum" + loopCount.ToString();
                    textBoxLineNum.Text = lineCount.ToString();
                    textBoxLineNum.Enabled = false;
                    textBoxLineNum.BackColor = Color.White;
                    textBoxLineNum.Width = 50;
                    textBoxLineNum.Location = new Point(105, yvar);
                    panelResults.Controls.Add(textBoxLineNum);

                    TextBox textBoxJobNum = new TextBox();
                    textBoxJobNum.Name = "txtJobNum" + loopCount.ToString();
                    textBoxJobNum.Width = 100;
                    textBoxJobNum.Text = GetJobNumberFromDescription(lineItem.Description);
                    textBoxJobNum.Location = new Point(195, yvar);
                    panelResults.Controls.Add(textBoxJobNum);

                    TextBox textBoxQuantity = new TextBox();
                    textBoxQuantity.Name = "txtQuantity" + loopCount.ToString();
                    textBoxQuantity.Width = 100;
                    textBoxQuantity.Enabled = false;
                    textBoxQuantity.BackColor = Color.White;
                    textBoxQuantity.Text = String.Format("{0:N2}", lineItem.Quantity);
                    textBoxQuantity.Location = new Point(300, yvar);
                    panelResults.Controls.Add(textBoxQuantity);

                    TextBox textBoxUnitPrice = new TextBox();
                    textBoxUnitPrice.Name = "txtUnitPrice" + loopCount.ToString();
                    textBoxUnitPrice.Width = 100;
                    textBoxUnitPrice.Enabled = false;
                    textBoxUnitPrice.BackColor = Color.White;
                    textBoxUnitPrice.Text = String.Format("{0:N2}", lineItem.UnitAmount);
                    textBoxUnitPrice.Location = new Point(405, yvar);
                    panelResults.Controls.Add(textBoxUnitPrice);

                    TextBox textBoxSupplierNum = new TextBox();
                    textBoxSupplierNum.Name = "txtSupplierNum" + loopCount.ToString();
                    textBoxSupplierNum.Width = 100;
                    textBoxSupplierNum.Text = txtSupplierNumber.Text;
                    textBoxSupplierNum.Location = new Point(510, yvar);
                    panelResults.Controls.Add(textBoxSupplierNum);

                    TextBox textBoxBuyerNum = new TextBox();
                    textBoxBuyerNum.Name = "txtBuyerNum" + loopCount.ToString();
                    textBoxBuyerNum.Width = 100;
                    textBoxBuyerNum.Text = txtBuyerNumber.Text;
                    textBoxBuyerNum.Location = new Point(615, yvar);
                    panelResults.Controls.Add(textBoxBuyerNum);

                    TextBox textBoxDueDate = new TextBox();
                    textBoxDueDate.Name = "txtDueDate" + loopCount.ToString();
                    textBoxDueDate.Width = 100;
                    textBoxDueDate.Text = dteDueDate.Value.ToString("dd/MM/yy");
                    textBoxDueDate.Location = new Point(720, yvar);
                    panelResults.Controls.Add(textBoxDueDate);

                    loopCount++;
                    lineCount++;
                }
            }
            txtRowCount.Text = loopCount.ToString();

        }

        private async void btnExtract_Click(object sender, EventArgs e)
        {

            if (String.IsNullOrEmpty(SPTExtract.Properties.Settings.Default.folder))
            {
                MessageBox.Show("You must select a folder to save the CSV file to, click the Folder button", "SPTExtract", MessageBoxButtons.OK);
                return;
            }

            TextBox textBoxInvNumberFirst;
            TextBox textBoxIdFirst;
            String currentInv = null;
            Guid currentId = Guid.Empty;
            try
            {
                textBoxInvNumberFirst = (TextBox)panelResults.Controls.Find("txtInvNum0", false)[0];
                currentInv = textBoxInvNumberFirst.Text;
                textBoxIdFirst = (TextBox)panelResults.Controls.Find("txtId0", false)[0];
                currentId = new Guid(textBoxIdFirst.Text);
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "btnExtract - missing initial controls");
                MessageBox.Show("No results to extract. Please run the invoice search first.", "SPTExtract", MessageBoxButtons.OK);
                return;
            }

            String path = SPTExtract.Properties.Settings.Default.folder + @"\SPTExtract_" + currentInv + "_" + DateTime.Now.ToString("dd-MM-yyyy HH-mm-ss") + ".csv";
            StreamWriter file = null;
            try
            {
                file = new StreamWriter(path);
                file.WriteLine("JobNum,Qty,UnitPriceGBP,Supplier,Buyer,DueDate");
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "btnExtract - failed to create output file");
                MessageBox.Show("Failed to create output file. Check folder permissions and path.", "SPTExtract", MessageBoxButtons.OK);
                try { file?.Dispose(); } catch { }
                return;
            }
            int rowsInThisFile = 0;

            // get a token
            XeroOAuth2Token xeroToken = null;
            try
            {
                xeroToken = TokenUtilities.GetStoredToken();
                if (xeroToken == null) throw new Exception("No Xero token available");

                var utcTimeNow = DateTime.UtcNow;

                if (utcTimeNow > xeroToken.ExpiresAtUtc)
                {
                    var client = new XeroClient(XeroConfig);
                    xeroToken = (XeroOAuth2Token)await client.RefreshAccessTokenAsync(xeroToken);
                    TokenUtilities.StoreToken(xeroToken);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "btnExtract - Token handling failed");
                MessageBox.Show("Failed to retrieve or refresh Xero token. See log for details.", "SPTExtract", MessageBoxButtons.OK);
                return;
            }

            var AccountingApi = new AccountingApi();

            for (int i = 0; i < Convert.ToInt32(txtRowCount.Text); i++)
            {
                String csvLine = "";
                TextBox textBoxIdTemp = null;
                try
                {
                    textBoxIdTemp = (TextBox)panelResults.Controls.Find("txtId" + i.ToString(), false)[0];
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, $"btnExtract - missing control for row {i}");
                    continue;
                }

                Guid id;
                try { id = new Guid(textBoxIdTemp.Text); } catch (Exception ex) { Logger.Log(ex, "btnExtract - invalid GUID"); continue; }

                CheckBox checkLineTemp = null;
                try { checkLineTemp = (CheckBox)panelResults.Controls.Find("chkLine" + i.ToString(), false)[0]; } catch (Exception ex) { Logger.Log(ex, $"btnExtract - missing checkbox for row {i}"); continue; }

                TextBox textBoxNumberTemp = (TextBox)panelResults.Controls.Find("txtInvNum" + i.ToString(), false)[0];
                String invNum = textBoxNumberTemp.Text;
                TextBox textBoxJobNumberTemp = (TextBox)panelResults.Controls.Find("txtJobNum" + i.ToString(), false)[0];
                String jobNum = textBoxJobNumberTemp.Text;
                TextBox textQuantityTemp = (TextBox)panelResults.Controls.Find("txtQuantity" + i.ToString(), false)[0];
                String quantity = textQuantityTemp.Text.Replace(",", String.Empty);
                TextBox textBoxUnitPriceTemp = (TextBox)panelResults.Controls.Find("txtUnitPrice" + i.ToString(), false)[0];
                String unitPrice = textBoxUnitPriceTemp.Text.Replace(",", String.Empty);
                TextBox textSupplierNumTemp = (TextBox)panelResults.Controls.Find("txtSupplierNum" + i.ToString(), false)[0];
                String supplierNum = textSupplierNumTemp.Text;
                TextBox textBoxBuyerNumTemp = (TextBox)panelResults.Controls.Find("txtBuyerNum" + i.ToString(), false)[0];
                String buyerNum = textBoxBuyerNumTemp.Text;
                TextBox textBoxDueDateTemp = (TextBox)panelResults.Controls.Find("txtDueDate" + i.ToString(), false)[0];
                String dueDate = Convert.ToDateTime(textBoxDueDateTemp.Text).ToString("dd/MM/yyyy");
                csvLine = jobNum + "," + quantity + "," + unitPrice + "," + supplierNum + "," + buyerNum + "," + dueDate;

                if (currentInv != invNum)
                {
                    file.Close();
                    // did we just create a file with no rows? delete it
                    if (rowsInThisFile == 0)
                    {
                        System.IO.File.Delete(path);
                    }
                    // did we just create a file with at least one row? Then mark the invoice as approved
                    if (rowsInThisFile > 0)
                    {
                        var response = await AccountingApi.GetInvoiceAsync(xeroToken.AccessToken, TokenUtilities.GetCurrentTenantId().ToString(), currentId);
                        Invoice invoice = response._Invoices[0];
                        invoice.Status = Invoice.StatusEnum.SUBMITTED;
                        List<Invoice> invoiceList = new List<Invoice>();
                        invoiceList.Add(invoice);
                        Invoices invoices = new Invoices();
                        invoices._Invoices = invoiceList;

                        response = await AccountingApi.UpdateInvoiceAsync(xeroToken.AccessToken, TokenUtilities.GetCurrentTenantId().ToString(), currentId, invoices);
                    }

                    path = SPTExtract.Properties.Settings.Default.folder + @"\SPTExtract_" + invNum + "_" + DateTime.Now.ToString("dd-MM-yyyy HH-mm-ss") + ".csv";
                    file = new StreamWriter(path);
                    file.WriteLine("jobNum,Qty,UnitPriceGBP,supplier,buyer,DueDate");
                    rowsInThisFile = 0;
                }

                if (checkLineTemp.Checked)
                {
                    file.WriteLine(csvLine);
                    rowsInThisFile++;
                }
                currentInv = invNum;
                currentId = id;
            }
            file.Close();
            // did we just create a file with no rows? delete it
            if (rowsInThisFile == 0)
            {
                System.IO.File.Delete(path);
            }
            // did we just create a file with at least one row? Then mark the invoice as approved
            if (rowsInThisFile > 0)
            {
                var response = await AccountingApi.GetInvoiceAsync(xeroToken.AccessToken, TokenUtilities.GetCurrentTenantId().ToString(), currentId);
                Invoice invoice = response._Invoices[0];
                invoice.Status = Invoice.StatusEnum.SUBMITTED;
                List<Invoice> invoiceList = new List<Invoice>();
                invoiceList.Add(invoice);
                Invoices invoices = new Invoices();
                invoices._Invoices = invoiceList;

                response = await AccountingApi.UpdateInvoiceAsync(xeroToken.AccessToken, TokenUtilities.GetCurrentTenantId().ToString(), currentId, invoices);

            }
            MessageBox.Show("Extract complete and selected invoices have been changed from Draft to Awaiting Approval in Xero. File extracted to:" + Environment.NewLine + SPTExtract.Properties.Settings.Default.folder, "SPTExtract", MessageBoxButtons.OK);

        }

        private String GetJobNumberFromDescription(String description)
        {
            String jobNum = "";
            try
            {
                if (!String.IsNullOrEmpty(description))
                {
                    int dashPos = description.LastIndexOf('-');
                    if (dashPos > -1)
                    {
                        jobNum = description.Substring(dashPos, description.Length - dashPos);
                        jobNum = jobNum.Replace('-', ' ');
                        jobNum = jobNum.Trim(' ');
                        // Remove all newlines from the 'example' string variable
                        jobNum = jobNum.Replace("\n", "");
                        jobNum = jobNum.Replace("\r", "");
                    }
                }
            }
            catch (Exception)
            {
                return jobNum;
            }

            return jobNum;
        }

        private void toolStripMenuItemAbout_Click(object sender, EventArgs e)
        {
            String message = "SPTExtract (v2.03) is a simple tool to get invoices from Xero and extract them to a csv file so that this can then be shared with a customer."
            + Environment.NewLine
            + Environment.NewLine
            + "If you have any issues using SPTExtract contact John Leach."
            ;

            MessageBox.Show(message, "SPTExtract", MessageBoxButtons.OK);
        }

        private async void toolStripMenuItemXecd C:\Code\SPTExtract-master\SPTExtract-masterro_Click(object sender, EventArgs e)
        {
            // get org name
            var xeroToken = TokenUtilities.GetStoredToken();
            var utcTimeNow = DateTime.UtcNow;

            if (utcTimeNow > xeroToken.ExpiresAtUtc)
            {
                var client = new XeroClient(XeroConfig);
                xeroToken = (XeroOAuth2Token)await client.RefreshAccessTokenAsync(xeroToken);
                TokenUtilities.StoreToken(xeroToken);
            }

            var AccountingApi = new AccountingApi();

            // load org name
            var response = await AccountingApi.GetOrganisationsAsync(xeroToken.AccessToken, TokenUtilities.GetCurrentTenantId().ToString());
            var org = response._Organisations;

            String shortCode = org[0].ShortCode;

            String targetURL = @"https://go.xero.com/organisationlogin/default.aspx?shortcode=" + shortCode + "&redirecturl=/Accounts/Receivable/Dashboard/";
            System.Diagnostics.Process.Start(targetURL);
        }

        private async void frmSPTExract_Load(object sender, EventArgs e)
        {
            txtRowCount.Visible = false;
            this.BackgroundImage = Properties.Resources.spt_background;

            DateTime tomorrow = DateTime.Now.AddDays(1);
            dteDueDate.Value = tomorrow;

            txtSupplierNumber.Text = SPTExtract.Properties.Settings.Default.supplierNumber;
            txtBuyerNumber.Text = SPTExtract.Properties.Settings.Default.buyerNumber;
            lblFolder.Text = SPTExtract.Properties.Settings.Default.folder;

            if (TokenUtilities.TokenExists())
            {
                LoadCustomers();
                // get org name
                var xeroToken = TokenUtilities.GetStoredToken();
                var utcTimeNow = DateTime.UtcNow;

                if (utcTimeNow > xeroToken.ExpiresAtUtc)
                {
                    var client = new XeroClient(XeroConfig);
                    xeroToken = (XeroOAuth2Token)await client.RefreshAccessTokenAsync(xeroToken);
                    TokenUtilities.StoreToken(xeroToken);
                }

                var AccountingApi = new AccountingApi();

                // load org name
                var response = await AccountingApi.GetOrganisationsAsync(xeroToken.AccessToken, TokenUtilities.GetCurrentTenantId().ToString());
                var org = response._Organisations;
                TenantName = org[0].Name;
            }
        }

        public async void LoadCustomers()
        {
            if (TokenUtilities.TokenExists())
            {
                // check token
                var xeroToken = TokenUtilities.GetStoredToken();
                var utcTimeNow = DateTime.UtcNow;

                if (utcTimeNow > xeroToken.ExpiresAtUtc)
                {
                    var client = new XeroClient(XeroConfig);
                    xeroToken = (XeroOAuth2Token)await client.RefreshAccessTokenAsync(xeroToken);
                    TokenUtilities.StoreToken(xeroToken);
                }

                var AccountingApi = new AccountingApi();

                // load customers
                String where = @"IsCustomer=true";
                try
                {
                    var response = await AccountingApi.GetContactsAsync(xeroToken.AccessToken, TokenUtilities.GetCurrentTenantId().ToString(), null, where);
                    var contacts = response._Contacts;
                    var contactsPaged = new List<Xero.NetStandard.OAuth2.Model.Accounting.Contact>();

                    comboBoxCustomer.DisplayMember = "Name"; // Column Name
                    comboBoxCustomer.ValueMember = "ContactId";  // Column Name
                    comboBoxCustomer.DataSource = contacts.OrderBy(o => o.Name).ToList();

                    if (!String.IsNullOrEmpty(SPTExtract.Properties.Settings.Default.contactId))
                    {
                        comboBoxCustomer.SelectedValue = new Guid(SPTExtract.Properties.Settings.Default.contactId);
                    }
                }
                catch (Exception)
                {
                    // clear down the token so we can start again
                    TokenUtilities.DestroyToken();
                    throw;
                }
            }
        }

        private void frmSPTExract_FormClosed(object sender, FormClosedEventArgs e)
        {
            SPTExtract.Properties.Settings.Default.supplierNumber = txtSupplierNumber.Text;
            SPTExtract.Properties.Settings.Default.buyerNumber = txtBuyerNumber.Text;
            if (comboBoxCustomer.SelectedItem != null)
            {
                SPTExtract.Properties.Settings.Default.contactId = comboBoxCustomer.SelectedValue.ToString();
            }
            SPTExtract.Properties.Settings.Default.Save();
        }

        private void btnFolder_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                DialogResult result = fbd.ShowDialog();

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    SPTExtract.Properties.Settings.Default.folder = fbd.SelectedPath;
                    lblFolder.Text = fbd.SelectedPath;
                    SPTExtract.Properties.Settings.Default.Save();
                }
            }
        }
    }

}
