using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Xero.NetStandard.OAuth2.Token;

public static class TokenUtilities
{
  [Serializable]
  public struct State
  {  
    public string state {get; set;}
    public State(string state){
      this.state = state;
    }
  }
  public static void StoreToken(XeroOAuth2Token xeroToken)
  {
    try
    {
      string serializedXeroToken = JsonSerializer.Serialize(xeroToken);
      System.IO.File.WriteAllText(Path.Combine(Application.UserAppDataPath, "xerotoken.json"), serializedXeroToken);
            Logger.Log("Stored token to xerotoken.json file", "StoreToken");
        }
    catch (Exception ex)
    {
      Logger.Log(ex, "StoreToken failed");
      // Don't crash the app if we can't persist the token
    }
  }

  public static XeroOAuth2Token GetStoredToken()
  {
    var xeroToken = new XeroOAuth2Token();
    
    try
    {
      string path = Path.Combine(Application.UserAppDataPath, "xerotoken.json");
      if (!File.Exists(path)) return null;

      string serializedXeroToken = System.IO.File.ReadAllText(path);
        if(serializedXeroToken.Length == 0)
            {
                Logger.Log("The xerotoken.json file exists but is empty", "GetStoredToken failed");
                return null;
            }
      xeroToken = JsonSerializer.Deserialize<XeroOAuth2Token>(serializedXeroToken);

      return xeroToken;
    }
    catch (Exception ex)
    {
      Logger.Log(ex, "GetStoredToken failed");
      try { MessageBox.Show("An error occurred while retrieving the Xero token. Please see the log file.", "SPTExtract", MessageBoxButtons.OK); } catch { }
      return null;
    }
  }

  public static bool TokenExists()
  {
    try
    {
      string serializedXeroTokenPath = Path.Combine(Application.UserAppDataPath, "xerotoken.json");
      bool fileExist = File.Exists(serializedXeroTokenPath);

            string serializedXeroToken = System.IO.File.ReadAllText(serializedXeroTokenPath);
            if (serializedXeroToken.Length == 0)
            {
                Logger.Log("The xerotoken.json file exists but is empty", "GetStoredToken failed");
                return false;
            }

            return fileExist;
    }
    catch (Exception ex)
    {
      Logger.Log(ex, "TokenExists failed");
      return false;
    }
  }

  public static void DestroyToken()
  {
    try
    {
      string serializedXeroTokenPath = Path.Combine(Application.UserAppDataPath, "xerotoken.json");
      if (File.Exists(serializedXeroTokenPath)) File.Delete(serializedXeroTokenPath); 
            Logger.Log("Delted xerotoken.json file", "DestroyToken");
        }
    catch (Exception ex)
    {
      Logger.Log(ex, "DestroyToken failed");
    }

    return;
  }

  private class TenantId
  {
    public Guid CurrentTenantId { get; set; }
  }

  public static void StoreTenantId(Guid tenantId)
  {
    try
    {
      string serializedXeroToken = JsonSerializer.Serialize(
        new TenantId { CurrentTenantId = tenantId }
      );
      System.IO.File.WriteAllText(Path.Combine(Application.UserAppDataPath, "tenantid.json"), serializedXeroToken);
            Logger.Log("Stored tenantId " + tenantId.ToString() + " to tenantid.json file", "StoreTenantId");
        }
    catch (Exception ex)
    {
      Logger.Log(ex, "StoreTenantId failed");
    }
  }

  public static Guid GetCurrentTenantId()
  {
    Guid id;
    try
    {
      string path = Path.Combine(Application.UserAppDataPath, "tenantid.json");
      if (!File.Exists(path)) return Guid.Empty;

      string serializedIndexFile = System.IO.File.ReadAllText(path);
      id = JsonSerializer.Deserialize<TenantId>(serializedIndexFile).CurrentTenantId;
    }
    catch (Exception ex)
    {
      Logger.Log(ex, "GetCurrentTenantId failed");
      id = Guid.Empty;
    }

    return id;
  }

  public static void StoreState(string state)
  {
    try
    {
      State currentState = new State(state);
      string serializedState = JsonSerializer.Serialize(currentState);
      System.IO.File.WriteAllText(Path.Combine(Application.UserAppDataPath, "state.json"), serializedState);
    }
    catch (Exception ex)
    {
      Logger.Log(ex, "StoreState failed");
    }
  }

  public static string GetCurrentState()
  {
    string state;
    try
    {
      string path = Path.Combine(Application.UserAppDataPath, "state.json");
      if (!File.Exists(path)) return null;

      string serializedIndexFile = System.IO.File.ReadAllText(path);
      state = JsonSerializer.Deserialize<State>(serializedIndexFile).state;
    }
    catch (Exception ex)
    {
      Logger.Log(ex, "GetCurrentState failed");
      state = null;
    }

    return state;
  }
}
