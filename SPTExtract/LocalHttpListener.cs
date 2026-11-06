using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xero.NetStandard.OAuth2.Token;
using Xero.NetStandard.OAuth2.Models;

namespace SPTExtract
{
    //this is based on code found at http://www.gabescode.com/dotnet/2018/11/01/basic-HttpListener-web-service.html 
    //made by https://stackoverflow.com/users/1202807/gabriel-luci thank you

    public class LocalHttpListener
    {
        private const int Port = 8888;

        private static readonly HttpListener Listener = new HttpListener { Prefixes = { $"http://localhost:{Port}/" } };

        private static bool _keepGoing = true;

        private static Task _mainLoop;

        private string _clientId;
        public string ClientId
        {
            get => _clientId;

            set
            {
                _clientId = value;
            }
        }

        public static async Task StartWebServer(frmSPTExract frm1)
        {
            if (_mainLoop != null && !_mainLoop.IsCompleted) return; //Already started
            {
                _mainLoop = MainLoop(frm1);
            }
        }

        public static void StopWebServer()
        {
            _keepGoing = false;
            lock (Listener)
            {
                //Use a lock so we don't kill a request that's currently being processed
                Listener.Stop();
            }
            try
            {
                _mainLoop.Wait();
            }
            catch { /* je ne care pas */ }
        }

        private static async Task MainLoop(frmSPTExract frm1)
        {
            Listener.Start();

            while (_keepGoing)
            {
                try
                {
                    //GetContextAsync() returns when a new request come in
                    var context = await Listener.GetContextAsync();
                    lock (Listener)
                    {
                        if (_keepGoing) ProcessRequest(context, frm1);
                    }
                }
                catch (Exception e)
                {
                    //this gets thrown when the listener is stopped
                    if (e is HttpListenerException)
                    {
                        return;
                    } 
                    Logger.Log(e, "LocalHttpListener.MainLoop exception");
                }
            }
        }

        private static void ProcessRequest(HttpListenerContext context, frmSPTExract frm1)
        {
            using (var response = context.Response)
            {
                try
                {
                    var handled = false;
                    switch (context.Request.Url.AbsolutePath)
                    {
                        //This is where we do different things depending on the URL
                        case "/callback":
                            handled = HandleCallbackRequest(context, frm1, response);
                            break;
                    }

                    if (!handled)
                    {
                        response.StatusCode = 404;
                    }
                }
                catch (Exception e)
                {
                    //Return the exception details the client - you may or may not want to do this
                    response.StatusCode = 500;
                    response.ContentType = "application/json";

                    var buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(e));
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);

                    Logger.Log(e, "LocalHttpListener.ProcessRequest exception");
                }

                // get tokens (run in background)
                try
                {
                    _ = GetTokensAndTenant(frm1);
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Failed to start GetTokensAndTenant");
                }
            }
        }

        private static bool HandleCallbackRequest(HttpListenerContext context, frmSPTExract frm1, HttpListenerResponse response)
        {
            response.ContentType = "text/html";
            
            //Write it to the response stream
            var query = context.Request.Url.Query;
            var code = "";
            var state = "";

            if (query.Contains("?"))
            {
                query = query.Substring(query.IndexOf('?') + 1);
            }

            foreach (var vp in Regex.Split(query, "&"))
            {
                var singlePair = Regex.Split(vp, "=");

                if (singlePair.Length == 2)
                {
                    if (singlePair[0] == "code")
                    {
                        code = singlePair[1];
                        frm1.AuthorisationCode = code;
                    }

                    if (singlePair[0] == "state")
                    {
                        state = singlePair[1];
                        frm1.State = state;
                    }
                }
            }

            var buffer = Encoding.UTF8.GetBytes("Code = " + code + "<br />State = " + state);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);

            return true;
        }

        private static async Task GetTokensAndTenant(frmSPTExract frm1)
        {
            try
            {
                //exchange the code for a set of tokens
                const string url = "https://identity.xero.com/connect/token";

                var client = new HttpClient();
                try
                {
                    var formContent = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("grant_type", "authorization_code"),
                        new KeyValuePair<string, string>("client_id", frm1.XeroConfig.ClientId),
                        new KeyValuePair<string, string>("code", frm1.AuthorisationCode),
                        new KeyValuePair<string, string>("redirect_uri", frm1.RedirectUri),
                        new KeyValuePair<string, string>("code_verifier", frm1.CodeVerifier),
                    });

                    var response = await client.PostAsync(url, formContent);

                    //read the response and populate the boxes for each token
                    //could also parse the expiry here if required
                    var content = await response.Content.ReadAsStringAsync();
                    var tokens = JObject.Parse(content);


                    XeroOAuth2Token xeroToken = new XeroOAuth2Token();
                    xeroToken.AccessToken = tokens["access_token"]?.ToString();
                    xeroToken.RefreshToken = tokens["refresh_token"]?.ToString();
                    xeroToken.IdToken = tokens["id_token"]?.ToString();

                    // get tenant
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", xeroToken.AccessToken);

                    response = await client.GetAsync(Constants.XeroUrls.ConnectionsUrl);
                    content = await response.Content.ReadAsStringAsync();

                    xeroToken.Tenants = JsonConvert.DeserializeObject<List<Tenant>>(content);
                    //sort the tenants to latest connected at the end of collection
                    xeroToken.Tenants.Sort((x, y) => DateTime.Compare(x.UpdatedDateUtc, y.UpdatedDateUtc));
                    //set the tenant to use
                    frm1.TenantId = xeroToken.Tenants[xeroToken.Tenants.Count - 1].TenantId.ToString();
                    frm1.TenantName = xeroToken.Tenants[xeroToken.Tenants.Count - 1].TenantName;
                    //clean up any old tenants
                    for (int i = 0; i < xeroToken.Tenants.Count - 1; i++)
                    {
                        var connectionId = xeroToken.Tenants[i].id;
                        var urlDelete = "https://api.xero.com/connections/" + connectionId.ToString();

                        response = await client.DeleteAsync(urlDelete); // should return a 204 for success
                    }

                    TokenUtilities.StoreToken(xeroToken);
                    TokenUtilities.StoreTenantId(xeroToken.Tenants[xeroToken.Tenants.Count - 1].TenantId);

                    // load cust drop down now, in case we've just switched orgs or connecting for the first time
                    try
                    {
                        frm1.LoadCustomers();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(ex, "Failed to LoadCustomers after token exchange");
                    }
                }
                finally
                {
                    client.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "GetTokensAndTenant failed");
                try
                {
                    // Notify UI if possible
                    frm1?.Invoke(new Action(() => MessageBox.Show("Failed to acquire tokens. See log for details.", "SPTExtract", MessageBoxButtons.OK)));
                }
                catch { }
            }
        }
    }
}
