using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace MovieGraph
{
    // ---------- Modèles JSON TMDB (noms identiques aux champs de l'API) ----------

    [Serializable] public class TmdbMovie
    {
        public int id;
        public string title;
        public string release_date;
        public string poster_path;
        public string overview;
        public float popularity;
        public float vote_average;
        public int vote_count;
        public string character;
    }

    [Serializable] public class TmdbPerson
    {
        public int id;
        public string name;
        public string profile_path;
        public string character;
        public int order;
        public float popularity;
        public string known_for_department;
    }

    [Serializable] public class TmdbVideo
    {
        public string key, site, type, iso_639_1, name;
        public bool official;
    }

    [Serializable] class VideoList { public List<TmdbVideo> results; }
    [Serializable] class MovieSearchResult { public List<TmdbMovie> results; }
    [Serializable] class PersonSearchResult { public List<TmdbPerson> results; }
    [Serializable] class MovieCredits { public List<TmdbPerson> cast; }
    [Serializable] class PersonCredits { public List<TmdbMovie> cast; }

    /// <summary>
    /// Petit client TMDB : recherche, castings, filmographies, images (avec cache).
    /// Accepte une clé API v3 (32 caractères) ou un "Read Access Token" v4 (commence par "eyJ").
    /// </summary>
    public class TmdbClient
    {
        const string ApiBase = "https://api.themoviedb.org/3";
        const string ImageBase = "https://image.tmdb.org/t/p/";

        readonly string key;
        readonly string language;
        readonly Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();

        public TmdbClient(string apiKeyOrToken, string language = "fr-FR")
        {
            key = (apiKeyOrToken ?? "").Trim();
            this.language = language;
        }

        bool IsBearer => key.StartsWith("eyJ");

        IEnumerator GetJson(string path, string query, Action<string> onOk)
        {
            string url = $"{ApiBase}{path}?language={language}";
            if (!string.IsNullOrEmpty(query)) url += "&" + query;
            if (!IsBearer) url += "&api_key=" + key;

            using (var req = UnityWebRequest.Get(url))
            {
                if (IsBearer) req.SetRequestHeader("Authorization", "Bearer " + key);
                req.SetRequestHeader("Accept", "application/json");
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[TMDB] {path} : {req.responseCode} {req.error}\n{req.downloadHandler?.text}");
                    yield break;
                }
                onOk(req.downloadHandler.text);
            }
        }

        public IEnumerator SearchMovie(string title, Action<TmdbMovie> onResult)
        {
            yield return GetJson("/search/movie", "query=" + UnityWebRequest.EscapeURL(title), json =>
            {
                var r = JsonUtility.FromJson<MovieSearchResult>(json);
                onResult(r?.results != null && r.results.Count > 0 ? r.results[0] : null);
            });
        }

        public IEnumerator SearchPerson(string name, Action<TmdbPerson> onResult)
        {
            yield return GetJson("/search/person", "query=" + UnityWebRequest.EscapeURL(name), json =>
            {
                var r = JsonUtility.FromJson<PersonSearchResult>(json);
                onResult(r?.results != null && r.results.Count > 0 ? r.results[0] : null);
            });
        }

        /// <summary>
        /// Meilleure bande-annonce YouTube d'un film (clé vidéo), en français de préférence,
        /// sinon en anglais. null s'il n'y en a pas.
        /// </summary>
        public IEnumerator GetTrailerKeys(int movieId, Action<List<string>> onResult)
        {
            List<TmdbVideo> videos = null;
            yield return GetJson($"/movie/{movieId}/videos", "include_video_language=fr,en,null",
                json => videos = JsonUtility.FromJson<VideoList>(json)?.results);

            // Toutes les bandes-annonces YouTube, de la meilleure à la moins bonne
            // (si une vidéo refuse d'être intégrée, on essaie la suivante)
            var keys = new List<string>();
            if (videos != null)
            {
                var candidates = new List<(int score, string key)>();
                foreach (var v in videos)
                {
                    if (v.site != "YouTube" || string.IsNullOrEmpty(v.key)) continue;
                    if (v.type != "Trailer" && v.type != "Teaser") continue;
                    int score = (v.iso_639_1 == "fr" ? 100 : 0) + (v.type == "Trailer" ? 10 : 0) + (v.official ? 1 : 0);
                    candidates.Add((score, v.key));
                }
                candidates.Sort((x, y) => y.score.CompareTo(x.score));
                foreach (var c in candidates) keys.Add(c.key);
            }
            onResult(keys);
        }

        /// <summary>Films tendance de la semaine (pour le menu de départ).</summary>
        public IEnumerator GetTrendingMovies(Action<List<TmdbMovie>> onResult)
        {
            yield return GetJson("/trending/movie/week", null, json =>
                onResult(JsonUtility.FromJson<MovieSearchResult>(json)?.results ?? new List<TmdbMovie>()));
        }

        /// <summary>Personnalités tendance de la semaine (pour le menu de départ).</summary>
        public IEnumerator GetTrendingPeople(Action<List<TmdbPerson>> onResult)
        {
            yield return GetJson("/trending/person/week", null, json =>
                onResult(JsonUtility.FromJson<PersonSearchResult>(json)?.results ?? new List<TmdbPerson>()));
        }

        public IEnumerator GetMovieCast(int movieId, Action<List<TmdbPerson>> onResult)
        {
            yield return GetJson($"/movie/{movieId}/credits", null, json =>
                onResult(JsonUtility.FromJson<MovieCredits>(json)?.cast ?? new List<TmdbPerson>()));
        }

        public IEnumerator GetPersonMovies(int personId, Action<List<TmdbMovie>> onResult)
        {
            yield return GetJson($"/person/{personId}/movie_credits", null, json =>
                onResult(JsonUtility.FromJson<PersonCredits>(json)?.cast ?? new List<TmdbMovie>()));
        }

        /// <summary>Télécharge une affiche / photo (taille w92, w185, w342...).</summary>
        public IEnumerator GetImage(string imagePath, string size, Action<Texture2D> onResult)
        {
            if (string.IsNullOrEmpty(imagePath)) { onResult(null); yield break; }

            string url = ImageBase + size + imagePath;
            if (textureCache.TryGetValue(url, out var cached)) { onResult(cached); yield break; }

            using (var req = UnityWebRequestTexture.GetTexture(url))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[TMDB] image {url} : {req.error}");
                    onResult(null);
                    yield break;
                }
                var tex = DownloadHandlerTexture.GetContent(req);
                tex.wrapMode = TextureWrapMode.Clamp;
                textureCache[url] = tex;
                onResult(tex);
            }
        }
    }
}
