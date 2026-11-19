using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

// Camada mínima sobre a biblioteca nativa SQLite (sem pacotes NuGet).
namespace Etl.Native
{
    static class N
    {
        const string Lib = "sqlite3";
        static N()
        {
            NativeLibrary.SetDllImportResolver(typeof(N).Assembly, (name, asm, path) =>
            {
                if (name != Lib) return IntPtr.Zero;
                string[] cands = { "winsqlite3", "sqlite3", "libsqlite3.so.0", "libsqlite3.so", "libsqlite3.dylib", "e_sqlite3" };
                foreach (var c in cands)
                    if (NativeLibrary.TryLoad(c, asm, path, out var h)) return h;
                foreach (var c in new[] { @"C:\Windows\System32\winsqlite3.dll", "/usr/lib/x86_64-linux-gnu/libsqlite3.so.0", "/usr/lib/libsqlite3.so.0", "/lib/x86_64-linux-gnu/libsqlite3.so.0", "/usr/lib/aarch64-linux-gnu/libsqlite3.so.0" })
                    if (NativeLibrary.TryLoad(c, out var h)) return h;
                throw new DllNotFoundException("Biblioteca nativa SQLite não encontrada (winsqlite3/libsqlite3).");
            });
        }
        [DllImport(Lib)] public static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Lib)] public static extern int sqlite3_close(IntPtr db);
        [DllImport(Lib)] public static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr cb, IntPtr arg, out IntPtr err);
        [DllImport(Lib)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Lib)] public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int n, out IntPtr stmt, IntPtr tail);
        [DllImport(Lib)] public static extern int sqlite3_step(IntPtr stmt);
        [DllImport(Lib)] public static extern int sqlite3_reset(IntPtr stmt);
        [DllImport(Lib)] public static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport(Lib)] public static extern int sqlite3_bind_null(IntPtr stmt, int i);
        [DllImport(Lib)] public static extern int sqlite3_bind_int64(IntPtr stmt, int i, long v);
        [DllImport(Lib)] public static extern int sqlite3_bind_double(IntPtr stmt, int i, double v);
        [DllImport(Lib)] public static extern int sqlite3_bind_text(IntPtr stmt, int i, byte[] s, int n, IntPtr destructor);
        [DllImport(Lib)] public static extern int sqlite3_bind_parameter_index(IntPtr stmt, byte[] name);
        public static byte[] Z(string s) => Encoding.UTF8.GetBytes(s + "\0");
    }
}

namespace Etl
{
    using Etl.Native;

    sealed class SqliteParameter
    {
        public string Name;
        public object? Value;
        public SqliteParameter(string name, object? v) { Name = name; Value = v; }
    }

    sealed class ParamCollection
    {
        public readonly Dictionary<string, SqliteParameter> Map = new();
        public SqliteParameter this[string n] => Map[n];
        public void Add(SqliteParameter p) => Map[p.Name] = p;
    }

    sealed class SqliteTransaction : IDisposable
    {
        readonly SqliteConnection _c;
        public SqliteTransaction(SqliteConnection c) { _c = c; }
        public void Commit() => _c.Exec("COMMIT");
        public void Dispose() { }
    }

    sealed class SqliteCommand : IDisposable
    {
        readonly SqliteConnection _c;
        IntPtr _stmt;
        Dictionary<string, int>? _idx;
        public string CommandText = "";
        public SqliteTransaction? Transaction;
        public ParamCollection Parameters = new();
        public SqliteCommand(SqliteConnection c) { _c = c; }

        public int ExecuteNonQuery()
        {
            if (Parameters.Map.Count == 0) { _c.Exec(CommandText); return 0; }
            if (_stmt == IntPtr.Zero)
            {
                int rc = N.sqlite3_prepare_v2(_c.Handle, N.Z(CommandText), -1, out _stmt, IntPtr.Zero);
                if (rc != 0) throw new Exception("prepare: " + _c.Err());
                _idx = Parameters.Map.Keys.ToDictionary(k => k, k => N.sqlite3_bind_parameter_index(_stmt, N.Z(k)));
            }
            foreach (var p in Parameters.Map.Values)
            {
                int i = _idx![p.Name];
                int rc = p.Value switch
                {
                    null => N.sqlite3_bind_null(_stmt, i),
                    DBNull => N.sqlite3_bind_null(_stmt, i),
                    string s => BindText(i, s),
                    int v => N.sqlite3_bind_int64(_stmt, i, v),
                    long v => N.sqlite3_bind_int64(_stmt, i, v),
                    double v => N.sqlite3_bind_double(_stmt, i, v),
                    _ => throw new InvalidOperationException("tipo não suportado")
                };
                if (rc != 0) throw new Exception("bind: " + _c.Err());
            }
            int st = N.sqlite3_step(_stmt);
            N.sqlite3_reset(_stmt);
            if (st != 101) throw new Exception("step: " + _c.Err());
            return 1;
        }

        int BindText(int i, string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            // SQLITE_TRANSIENT = -1
            return N.sqlite3_bind_text(_stmt, i, b, b.Length, new IntPtr(-1));
        }

        public void Dispose() { if (_stmt != IntPtr.Zero) { N.sqlite3_finalize(_stmt); _stmt = IntPtr.Zero; } }
    }

    sealed class SqliteConnection : IDisposable
    {
        public IntPtr Handle;
        readonly string _path;
        public SqliteConnection(string path) { _path = path; }

        public void Open()
        {
            // READWRITE | CREATE
            int rc = N.sqlite3_open_v2(N.Z(_path), out Handle, 6, IntPtr.Zero);
            if (rc != 0) throw new Exception("open: " + Err());
        }
        public string Err() => Marshal.PtrToStringUTF8(N.sqlite3_errmsg(Handle)) ?? "erro desconhecido";
        public void Exec(string sql)
        {
            int rc = N.sqlite3_exec(Handle, N.Z(sql), IntPtr.Zero, IntPtr.Zero, out _);
            if (rc != 0) throw new Exception("exec: " + Err());
        }
        public SqliteCommand CreateCommand() => new(this);
        public SqliteTransaction BeginTransaction() { Exec("BEGIN"); return new SqliteTransaction(this); }
        public void Dispose() { if (Handle != IntPtr.Zero) { N.sqlite3_close(Handle); Handle = IntPtr.Zero; } }
    }
}
