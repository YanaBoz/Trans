using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using TrafficSimulation.Core.Models;
using TrafficSimulation.Core.Repositories;

namespace TrafficSimulation.Infrastructure.Data
{
    public class JsonParametersRepository : IParametersRepository
    {
        private readonly string _storagePath;
        private readonly object _lockObject = new object();

        public JsonParametersRepository(string storagePath = "Data/Parameters")
        {
            _storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, storagePath);
            Directory.CreateDirectory(_storagePath);
        }

        public async Task<SimulationParameters> GetParametersAsync(Guid id)
        {
            var filePath = Path.Combine(_storagePath, $"{id}.json");
            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonConvert.DeserializeObject<SimulationParameters>(json,
                    new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        NullValueHandling = NullValueHandling.Ignore
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading parameters {id}: {ex.Message}");
                return null;
            }
        }

        public async Task<IEnumerable<SimulationParameters>> GetAllParametersAsync()
        {
            lock (_lockObject)
            {
                var allFiles = Directory.GetFiles(_storagePath, "*.json");
                var parametersList = new List<SimulationParameters>();

                foreach (var file in allFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var parameters = JsonConvert.DeserializeObject<SimulationParameters>(json,
                            new JsonSerializerSettings
                            {
                                TypeNameHandling = TypeNameHandling.Auto,
                                NullValueHandling = NullValueHandling.Ignore
                            });

                        if (parameters != null)
                            parametersList.Add(parameters);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading parameters from {file}: {ex.Message}");
                    }
                }

                return parametersList.OrderByDescending(p => p.ModifiedDate).ToList();
            }
        }

        public async Task SaveParametersAsync(SimulationParameters parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            lock (_lockObject)
            {
                parameters.UpdateModifiedDate();

                if (string.IsNullOrEmpty(parameters.Name))
                    parameters.Name = $"Parameters_{DateTime.Now:yyyyMMdd_HHmmss}";

                var filePath = Path.Combine(_storagePath, $"{parameters.Id}.json");
                var json = JsonConvert.SerializeObject(parameters, Newtonsoft.Json.Formatting.Indented,
                    new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        NullValueHandling = NullValueHandling.Ignore
                    });

                File.WriteAllText(filePath, json);
                UpdateIndex(parameters).Wait();
            }
        }

        public async Task DeleteParametersAsync(Guid id)
        {
            lock (_lockObject)
            {
                var filePath = Path.Combine(_storagePath, $"{id}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                UpdateIndexAfterDeletion(id).Wait();
            }
        }

        private async Task UpdateIndex(SimulationParameters parameters)
        {
            lock (_lockObject)
            {
                var indexFile = Path.Combine(_storagePath, "_index.json");
                List<ParametersIndexEntry> index;

                if (File.Exists(indexFile))
                {
                    var indexJson = File.ReadAllText(indexFile);
                    index = JsonConvert.DeserializeObject<List<ParametersIndexEntry>>(indexJson) ?? new();
                }
                else
                {
                    index = new List<ParametersIndexEntry>();
                }

                var existing = index.FirstOrDefault(x => x.Id == parameters.Id);
                if (existing != null)
                {
                    existing.Name = parameters.Name;
                    existing.ModifiedDate = parameters.ModifiedDate;
                }
                else
                {
                    index.Add(new ParametersIndexEntry
                    {
                        Id = parameters.Id,
                        Name = parameters.Name,
                        CreatedDate = parameters.CreatedDate,
                        ModifiedDate = parameters.ModifiedDate
                    });
                }

                index = index.OrderByDescending(x => x.ModifiedDate).ToList();
                File.WriteAllText(indexFile, JsonConvert.SerializeObject(index, Newtonsoft.Json.Formatting.Indented));
            }
        }

        private async Task UpdateIndexAfterDeletion(Guid id)
        {
            lock (_lockObject)
            {
                var indexFile = Path.Combine(_storagePath, "_index.json");
                if (!File.Exists(indexFile))
                    return;

                var indexJson = File.ReadAllText(indexFile);
                var index = JsonConvert.DeserializeObject<List<ParametersIndexEntry>>(indexJson) ?? new();

                index.RemoveAll(x => x.Id == id);
                File.WriteAllText(indexFile, JsonConvert.SerializeObject(index, Newtonsoft.Json.Formatting.Indented));
            }
        }

        public async Task<SimulationParameters> CreateDefaultParametersAsync(string name = null)
        {
            var parameters = new SimulationParameters
            {
                Name = name ?? "Default Parameters",
                CreationDate = DateTime.Now
            };

            await SaveParametersAsync(parameters);
            return parameters;
        }

        public async Task<SimulationParameters> CloneParametersAsync(SimulationParameters parameters, string newName)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore
            };

            var json = JsonConvert.SerializeObject(parameters, settings);
            var clone = JsonConvert.DeserializeObject<SimulationParameters>(json, settings);

            if (clone == null)
                throw new InvalidOperationException("Failed to clone parameters");

            clone.Id = Guid.NewGuid();
            clone.Name = newName ?? $"{parameters.Name} - Copy";
            clone.CreatedDate = DateTime.Now;
            clone.ModifiedDate = DateTime.Now;

            await SaveParametersAsync(clone);
            return clone;
        }

        public async Task<IEnumerable<ParametersIndexEntry>> GetParametersIndexAsync()
        {
            lock (_lockObject)
            {
                var indexFile = Path.Combine(_storagePath, "_index.json");
                if (!File.Exists(indexFile))
                    return new List<ParametersIndexEntry>();

                var indexJson = File.ReadAllText(indexFile);
                return JsonConvert.DeserializeObject<List<ParametersIndexEntry>>(indexJson) ?? new();
            }
        }
    }

    public class ParametersIndexEntry
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
    }
}