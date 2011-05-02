#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2009 M. Wayne Walter
 * 
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 * 
 * Business use restricted to 30 days except as otherwise stated in
 * in your Service Level Agreement (SLA).
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, see <http://www.tickzoom.org/wiki/Licenses>
 * or write to Free Software Foundation, Inc., 51 Franklin Street,
 * Fifth Floor, Boston, MA  02110-1301, USA.
 * 
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace TickZoom.Api
{
    /// <summary>
    /// Description of ModelLoaderManager.
    /// </summary>
    public class Plugins
    {
        private List<Type> modelLoaders;
        private List<Type> models;
        private List<Type> serializers;
        private int errorCount = 0;
        private static readonly Log log = Factory.SysLog.GetLogger(typeof(Plugins));
        private readonly bool debug = log.IsDebugEnabled;
        private static Plugins plugins;
        private static object pluginsLocker = new object();
        private bool isInitialized = false;

        public string PluginFolder;

        /// <summary>
        /// Loads all plugins each time you get an instance
        /// so that plugins can be installed without restarting
        /// TickZoom.
        /// </summary>
        public static Plugins Instance
        {
            get
            {
                if (plugins == null)
                {
                    lock (pluginsLocker)
                    {
                        if (plugins == null)
                        {
                            plugins = new Plugins();
                        }
                    }
                }
                return plugins;
            }
        }

        private Plugins()
        {
            errorCount = 0;
            modelLoaders = new List<Type>();
            models = new List<Type>();
            serializers = new List<Type>();
        }

        private void Initialize()
        {
            if( isInitialized) return;
            string appData = Factory.Settings["AppDataFolder"];
            if (appData == null)
            {
                throw new ApplicationException("AppDataFolder was not set in app.config.");
            }
            PluginFolder = appData + @"\Plugins";
            Directory.CreateDirectory(PluginFolder);
            LoadAssemblies(PluginFolder);
            isInitialized = true;
        }

        public ModelLoaderInterface GetLoader(string name)
        {
            var loader = SearchLoaders(name);
            if( loader == null)
            {
                Initialize();
                loader = SearchLoaders(name);
            }
            if( loader == null)
            {
                throw new ApplicationException("ModelLoader '" + name + "' not found.");
            } else
            {
                return loader;
            }
        }

        private ModelLoaderInterface SearchLoaders(string name)
        {
            var count = 0;
            ModelLoaderInterface result = null;
            for (int i = 0; i < modelLoaders.Count; i++)
            {
                Type type = modelLoaders[i];
                var loader = (ModelLoaderInterface) Activator.CreateInstance(type);
                if (loader.Name.Equals(name))
                {
                    count++;
                    result = loader;
                }
            }
            if (count == 1)
            {
                return result;
            }
            else if (count > 0)
            {
                throw new ApplicationException("More than one ModelLoader '" + name + "' was found.");
            }
            else
            {
                return null;
            }
        }

        public class SerializerNotFoundException : Exception
        {
            public SerializerNotFoundException(string message)
                : base(message)
            {
            }
        }

        public Serializer GetSerializer(int eventType)
        {
            var serializer = SearchSerializers(eventType);
            if (serializer == null)
            {
                // Forces the provider factory to load, thereby copying
                // from AutoUpdate to the ShadowCopy Folder since it includes
                // some serializers.
                var providerFactory = Factory.Provider;
                Initialize();
                serializer = SearchSerializers(eventType);
            }
            if (serializer == null)
            {
                throw new SerializerNotFoundException("Serializer for " + (EventType)eventType + " not found.");
            }
            else
            {
                return serializer;
            }
        }

        private Serializer SearchSerializers(int eventType)
        {
            Serializer serializer = null;
            for (int i = 0; i < serializers.Count; i++)
            {
                Type type = serializers[i];
                try
                {
                    serializer = (Serializer)Activator.CreateInstance(type);
                    if (serializer.EventType == eventType)
                    {
                        return serializer;
                    }
                }
                catch (Exception ex)
                {
                    log.Warn("Stale serializer found. Coninuing. Error message: " + ex.Message);
                }
            }
            return serializer;
        }

        public ModelInterface GetModel(string name)
        {
            Initialize();
            for (int i = 0; i < models.Count; i++)
            {
                if (models[i].Name.Equals(name))
                {
                    return (ModelInterface)Activator.CreateInstance(models[i]);
                }
            }
            throw new Exception("Model '" + name + "' not found.");
        }

        private void LoadAssemblies(String path)
        {
            string currentDirectory = System.Environment.CurrentDirectory;
            errorCount = 0;
            modelLoaders = new List<Type>();
            models = new List<Type>();
            serializers = new List<Type>();

            // This loads plugins from the plugin folder
            List<string> files = new List<string>();
            files.AddRange(Directory.GetFiles(path, "*plugin*.dll", SearchOption.AllDirectories));
            //			files.AddRange( Directory.GetFiles(path, "*plugin*.dll", SearchOption.AllDirectories));
            // This loads plugins from the installation folder
            // so all the common models and modelloaders get loaded.
            files.AddRange(Directory.GetFiles(currentDirectory, "*plugin*.dll", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(currentDirectory, "*common*.dll", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(currentDirectory, "*test*.dll", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(currentDirectory, "*test*.exe", SearchOption.AllDirectories));


            foreach (String filename in files)
            {
                var nameListMap = new Dictionary<string, List<Type>>();
                nameListMap.Add("ModelLoaderInterface", modelLoaders);
                nameListMap.Add("ModelInterface", models);
                nameListMap.Add("Serializer", serializers);
                LoadImplementations(filename, nameListMap);
            }
            if (modelLoaders.Count == 0)
            {
                log.Warn("Zero ModelLoader plugins found in " + PluginFolder + " or " + currentDirectory);
            }
            if (serializers.Count == 0)
            {
                log.Warn("Zero Serializer plugins found in " + PluginFolder + " or " + currentDirectory);
            }
        }

        // Exit and Enter Common aren't directly accessible.
        // They provide support for custom strategies.

        void LoadImplementations(String filename, Dictionary<string, List<Type>> nameListMap)
        {
            if (debug) log.Debug("Loading " + filename);
            var t2 = typeof(object);
            try
            {
                var assembly = Assembly.LoadFrom(filename);
                foreach (var t in assembly.GetTypes())
                {
                    t2 = t;
                    foreach (var kvp in nameListMap)
                    {
                        var typeName = kvp.Key;
                        var list = kvp.Value;
                        if (t.IsClass && !t.IsAbstract)
                        {
                            if (t.GetInterface(typeName) != null)
                            {
                                try
                                {
                                    list.Add(t);
                                }
                                catch (MissingMethodException)
                                {
                                    errorCount++;
                                    log.Notice("ModelLoader '" + t.Name + "' in '" + filename + "' failed to load due to missing default constructor");
                                }
                            }
                        }
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                log.Warn("Plugin load failed for '" + t2.Name + "' in '" + filename + "' with loader exceptions:");
                for (int i = 0; i < ex.LoaderExceptions.Length; i++)
                {
                    Exception lex = ex.LoaderExceptions[i];
                    log.Warn(lex.ToString());
                }
            }
            catch (Exception err)
            {
                log.Warn("Plugin load failed for '" + t2.Name + "' in '" + filename + "': " + err.ToString());
            }
        }

        public int ErrorCount
        {
            get { return errorCount; }
        }

        public List<ModelLoaderInterface> GetLoaders()
        {
            if (modelLoaders.Count == 0)
            {
                Initialize();
            }
            List<ModelLoaderInterface> loaders = new List<ModelLoaderInterface>();
            for (int i = 0; i < modelLoaders.Count; i++)
            {
                loaders.Add((ModelLoaderInterface)Activator.CreateInstance(modelLoaders[i]));
            }
            return loaders;
        }

        public List<Type> Models
        {
            get { return models; }
        }

    }
}
