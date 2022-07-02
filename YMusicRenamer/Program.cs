using System.Data;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Linq;
using System.Data.Entity;

Console.Write("Enter tracks path:");
var trackPath = Console.ReadLine().Replace("\"", "");

Console.Write("Enter DB path:");
var dbPath = Console.ReadLine().Replace("\"", "");

using var dbCon = new SQLiteConnection(string.Format("Data Source={0};", new FileInfo(dbPath).FullName));
dbCon.Open();

Console.Write("Enter save path:");
var savePath = Console.ReadLine().Replace("\"", "");

Console.Write("Group in folders by Artist? 0 - 1:");
var flag = Console.ReadLine();
bool group = !flag.Contains("0");


List<Task> tasks = new();

var lock_obj = new object();
Dictionary<string, int> names = new();
foreach (var file in new DirectoryInfo(trackPath).GetFiles("*", SearchOption.AllDirectories))
    tasks.Add(Task.Run(() =>
    {
        try
        {
            var nfile = TagLib.File.Create(file.FullName);

            var id = file.Name.Replace(file.Extension, "");
            nfile.Tag.Title = Query($"select Title from T_Track where Id={id};");
            nfile.Tag.Lyrics = Query($"select FullLyrics from T_TrackLyrics where Id={id};");
            var preformersIds = QueryMultiple($"select ArtistId from T_TrackArtist where TrackId={id};");

            List<string> preformers = new();
            foreach (var aId in preformersIds)
            {
                var p = Query($"select Name from T_Artist where Id={aId}");
                if (p is not null)
                    preformers.Add(p);
            }

            nfile.Tag.Performers = preformers.ToArray();



            nfile.Tag.Album = Query($"select Title from T_Album where Id={Query($"select AlbumId from T_TrackAlbum where TrackId={id};")}");
            nfile.Tag.AlbumArtists = Query($"select ArtistsString from T_Album where Id={Query($"select AlbumId from T_TrackAlbum where TrackId={id};")}")
                ?.Split(",") ?? Array.Empty<string>();


            nfile.Save();

            string per = "";
            if (nfile.Tag.Performers.Length > 0)
                if (nfile.Tag.FirstPerformer is not null)
                    per = nfile.Tag.FirstPerformer;

            per = per.Replace("\\", "");
            per = per.Replace("/", "");
            var illegal = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            foreach (var c in illegal)
                per = per.Replace(c.ToString(), "");

            string tit = "";
                if (nfile.Tag.Title is not null)
                tit = nfile.Tag.Title;

            tit = tit.Replace("\\", "");
            tit = tit.Replace("/", "");
            foreach (var c in illegal)
                tit = tit.Replace(c.ToString(), "");


            string filename = "";
            if (per.Length > 0)
                filename = $"{per} - {tit}";
            else
                filename = tit;


            if (filename.Length == 0)
                filename = id;


            int sameCount = 0;
            lock (lock_obj)
            {
                if (!names.ContainsKey(filename))
                    names.Add(filename, 0);
                else
                    names[filename]++;

                sameCount = names[filename];
            }

            var savepath = savePath;

            if (group && per.Length > 0)
            { 
                savepath = savepath + per + "\\";

                lock (lock_obj)
                {
                    if (!Directory.Exists(savepath))
                        Directory.CreateDirectory(savepath);
                }
            }
                
            
            savepath = savepath + filename;
            if (sameCount > 0)
                savepath = savepath + " " + sameCount.ToString();

            savepath = savepath + file.Extension;

            file.CopyTo(savepath);

            Console.WriteLine(filename);
        }
        catch(Exception ex)
        {
            Console.WriteLine("----------- " + ex.Message + " -----------");
        }
    }));

await Task.WhenAll(tasks);

Console.WriteLine("Done!");

string[] QueryMultiple(string query)
{
    using var cmd = new SQLiteCommand(query, dbCon);
    try
    {
        return cmd.ExecuteReader().ToEnumerable().ToList().ToArray() ?? Array.Empty<string>();
    }
    catch
    {
        return Array.Empty<string>();
    };
}

string Query(string query)
{
    using var cmd = new SQLiteCommand(query, dbCon);
    try
    {
        return cmd.ExecuteScalar()?.ToString()?.Correct() ?? null;
    }
    catch 
    {
        return null;
    };
}


static class Ext
{
    public static IEnumerable<string> ToEnumerable(this IDataReader reader)
    {
        while (reader.Read())
            yield return reader[0].ToString();
    }

    public static string Correct(this string str)
    {
        if (str.Contains("☺"))
        {
            str = str + ")";
            str = str.Replace("☺", " (");
        }
        return str;   
    }
}
