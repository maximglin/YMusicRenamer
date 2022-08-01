using System.Data;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Linq;
using System.Data.Entity;
using System.Text;


try
{
    Console.Write("Enter path: ");
    var path = Console.ReadLine()?.Replace("\"", "") ?? throw new ArgumentNullException();
    var name = new DirectoryInfo(path).GetFiles("*.sqlite", SearchOption.AllDirectories).FirstOrDefault()?.
        Name.Replace("musicdb_", "").Replace(".sqlite", "") ?? throw new Exception();

    var dbPath = new DirectoryInfo(path).GetFiles("*.sqlite", SearchOption.AllDirectories).FirstOrDefault()?.FullName ?? throw new Exception();
    var trackPath = new DirectoryInfo(path).
        GetDirectories().Where(d => d.Name == "Music").FirstOrDefault()?.
        GetDirectories().Where(d => d.Name == name).FirstOrDefault()?.FullName ?? throw new Exception();

    var picFiles = new DirectoryInfo(path).
        GetDirectories().Where(d => d.Name == "CachedCovers").FirstOrDefault()?.
        GetFiles("*", SearchOption.AllDirectories) ?? throw new Exception();

    using var dbCon = new SQLiteConnection(string.Format("Data Source={0};", new FileInfo(dbPath).FullName));
    dbCon.Open();

    Console.Write("Enter save path:");
    var savePath = Console.ReadLine().Replace("\"", "");
    savePath = new DirectoryInfo(savePath).FullName + "/";

    Console.Write("Group in folders by Artist? 0 - 1:");
    var flag = Console.ReadLine();
    bool group = !flag.Contains("0");

    Console.Write("Translit into latin? 0 - 1:");
    var transl = Console.ReadLine();
    bool translit = !transl.Contains("0");

    bool translitTags = false;
    if(translit)
    {
        Console.Write("Translit tags in latin too? 0 - 1:");
        var transl2 = Console.ReadLine();
        translitTags = !transl2.Contains("0");
    }


    List<Task> tasks = new();

    var lock_obj = new object();
    Dictionary<string, int> names = new();
    foreach (var file in new DirectoryInfo(trackPath).GetFiles("*", SearchOption.TopDirectoryOnly))
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
                foreach (var aId in preformersIds.Reverse())
                {
                    var p = Query($"select Name from T_Artist where Id={aId}");
                    if (p is not null)
                        preformers.Add(p);
                }

                nfile.Tag.Performers = preformers.ToArray();

                var picture = Query($"select CoverUri from T_Track where Id={id};");
                if(picture is not null)
                {
                    foreach(var pic in picFiles.Where(p => p.Name.Contains(picture.Replace("%%", "").Replace("/", "¿"))).OrderByDescending(p => p.Name))
                    {
                        try
                        {
                            var picPath = pic?.FullName ?? null;
                            if (picPath is not null)
                            {
                                using var img = System.Drawing.Image.FromFile(picPath);
                                var converter = new System.Drawing.ImageConverter();
                                var bytes = (byte[])converter.ConvertTo(img, typeof(byte[]));

                                nfile.Tag.Pictures = new TagLib.IPicture[]
                                {
                            new TagLib.Picture(bytes)
                                };
                            }
                            break;
                        }
                        catch (Exception imgEx)
                        {
                            nfile.Tag.Pictures = Array.Empty<TagLib.IPicture>();
                        }
                    }
                    
                }


                nfile.Tag.Album = Query($"select Title from T_Album where Id={Query($"select AlbumId from T_TrackAlbum where TrackId={id};")}");
                nfile.Tag.AlbumArtists = Query($"select ArtistsString from T_Album where Id={Query($"select AlbumId from T_TrackAlbum where TrackId={id};")}")
                    ?.Split(",") ?? Array.Empty<string>();

                //set the first performer to main performer
                //under the assumption that album artist is main performer
                if (
                nfile.Tag.AlbumArtists.Length > 0 &&
                nfile.Tag.Performers.Length > 1
                ) 
                {
                    var fartist = nfile.Tag.AlbumArtists[0].ToLower();
                    if (nfile.Tag.Performers.Any(p => p.ToLower() == fartist))
                    {
                        var indexToSwap = nfile.Tag.Performers.Select((p, i) => new { Name = p, Index = i }).Where(p => p.Name.ToLower() == fartist).First().Index;
                        
                        if(indexToSwap != 0)
                        {
                            var arr = nfile.Tag.Performers;

                            var t = arr[0];
                            arr[0] = arr[indexToSwap];
                            arr[indexToSwap] = t;

                            nfile.Tag.Performers = arr;
                        }
                    }
                }


                if(translitTags)
                {
                    nfile.Tag.Performers = nfile.Tag.Performers.Select(s => s.Translit()).ToArray();
                    nfile.Tag.Title = nfile.Tag.Title.Translit();
                    nfile.Tag.Lyrics = nfile.Tag.Lyrics.Translit();
                    nfile.Tag.Album = nfile.Tag.Album.Translit();
                    nfile.Tag.AlbumArtists = nfile.Tag.AlbumArtists.Select(s => s.Translit()).ToArray();
                }


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

                
                if(!translitTags && translit)
                {
                    per = per.Translit();
                    tit = tit.Translit();
                }

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
            catch (Exception ex)
            {
                Console.WriteLine("----------- " + ex.Message + " -----------");
            }
        }));

    await Task.WhenAll(tasks);

    Console.WriteLine("Done!");
    Console.ReadKey(true);



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
}
catch
{
    Console.WriteLine("Error!");
    Console.ReadKey(true);
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

    public static string Translit(this string s)
    {
        return RussianTransliteration.RussianTransliterator.GetTransliteration(s);
    }
}
