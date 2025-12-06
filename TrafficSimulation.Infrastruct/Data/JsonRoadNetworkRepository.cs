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
    public class JsonRoadNetworkRepository : IRoadNetworkRepository
    {
        private readonly string _storagePath;
        private readonly object _lockObject = new object();

        public JsonRoadNetworkRepository(string storagePath = "Data/Networks")
        {
            _storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, storagePath);
            Directory.CreateDirectory(_storagePath);
        }

        public async Task<RoadNetwork> GetByIdAsync(Guid id)
        {
            var filePath = Path.Combine(_storagePath, $"{id}.json");
            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonConvert.DeserializeObject<RoadNetwork>(json,
                    new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        NullValueHandling = NullValueHandling.Ignore
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading network {id}: {ex.Message}");
                return null;
            }
        }

        public async Task<IEnumerable<RoadNetwork>> GetAllAsync()
        {
            lock (_lockObject)
            {
                var indexFile = Path.Combine(_storagePath, "_index.json");
                if (!File.Exists(indexFile))
                    return new List<RoadNetwork>();

                var indexJson = File.ReadAllText(indexFile);
                var index = JsonConvert.DeserializeObject<List<NetworkIndexEntry>>(indexJson) ?? new();

                var networks = new List<RoadNetwork>();
                foreach (var entry in index)
                {
                    try
                    {
                        var network = GetByIdAsync(entry.Id).Result;
                        if (network != null)
                            networks.Add(network);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading network {entry.Id}: {ex.Message}");
                    }
                }

                return networks;
            }
        }

        public async Task SaveAsync(RoadNetwork network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));

            lock (_lockObject)
            {
                network.UpdateModifiedDate();

                var filePath = Path.Combine(_storagePath, $"{network.Id}.json");
                var json = JsonConvert.SerializeObject(network, Newtonsoft.Json.Formatting.Indented,
                    new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        NullValueHandling = NullValueHandling.Ignore,
                        ContractResolver = new IgnoreNavigationPropertiesResolver()
                    });

                File.WriteAllText(filePath, json);
                UpdateIndex(network).Wait();
            }
        }

        private async Task UpdateIndex(RoadNetwork network)
        {
            lock (_lockObject)
            {
                var indexFile = Path.Combine(_storagePath, "_index.json");
                List<NetworkIndexEntry> index;

                if (File.Exists(indexFile))
                {
                    var indexJson = File.ReadAllText(indexFile);
                    index = JsonConvert.DeserializeObject<List<NetworkIndexEntry>>(indexJson) ?? new();
                }
                else
                {
                    index = new List<NetworkIndexEntry>();
                }

                var existing = index.FirstOrDefault(x => x.Id == network.Id);
                if (existing != null)
                {
                    existing.Name = network.Name;
                    existing.ModifiedDate = network.ModifiedDate;
                    existing.VertexCount = network.Vertices.Count;
                    existing.EdgeCount = network.Edges.Count;
                }
                else
                {
                    index.Add(new NetworkIndexEntry
                    {
                        Id = network.Id,
                        Name = network.Name,
                        CreatedDate = network.CreatedDate,
                        ModifiedDate = network.ModifiedDate,
                        VertexCount = network.Vertices.Count,
                        EdgeCount = network.Edges.Count
                    });
                }

                // Сортируем по дате изменения
                index = index.OrderByDescending(x => x.ModifiedDate).ToList();

                File.WriteAllText(indexFile, JsonConvert.SerializeObject(index, Newtonsoft.Json.Formatting.Indented));
            }
        }

        public async Task DeleteAsync(Guid id)
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

        private async Task UpdateIndexAfterDeletion(Guid id)
        {
            lock (_lockObject)
            {
                var indexFile = Path.Combine(_storagePath, "_index.json");
                if (!File.Exists(indexFile))
                    return;

                var indexJson = File.ReadAllText(indexFile);
                var index = JsonConvert.DeserializeObject<List<NetworkIndexEntry>>(indexJson) ?? new();

                index.RemoveAll(x => x.Id == id);

                File.WriteAllText(indexFile, JsonConvert.SerializeObject(index, Newtonsoft.Json.Formatting.Indented));
            }
        }

        public async Task<RoadNetwork> CloneAsync(RoadNetwork network, string newName)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));

            // Сериализуем и десериализуем для глубокого копирования
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new IgnoreNavigationPropertiesResolver()
            };

            var json = JsonConvert.SerializeObject(network, settings);
            var clone = JsonConvert.DeserializeObject<RoadNetwork>(json, settings);

            if (clone == null)
                throw new InvalidOperationException("Failed to clone network");

            clone.Id = Guid.NewGuid();
            clone.Name = newName ?? $"{network.Name} - Copy";
            clone.CreatedDate = DateTime.Now;
            clone.ModifiedDate = DateTime.Now;

            // Обновляем ID всех вершин и ребер в клоне
            UpdateIdsInClone(clone);

            await SaveAsync(clone);
            return clone;
        }

        private void UpdateIdsInClone(RoadNetwork clone)
        {
            var vertexIdMap = new Dictionary<Guid, Guid>();

            // Обновляем ID вершин
            foreach (var vertex in clone.Vertices)
            {
                var oldId = vertex.Id;
                vertex.Id = Guid.NewGuid();
                vertexIdMap[oldId] = vertex.Id;

                // Очищаем списки ребер
                vertex.IncomingEdges.Clear();
                vertex.OutgoingEdges.Clear();
            }

            // Обновляем ID ребер и их ссылки
            foreach (var edge in clone.Edges)
            {
                var oldId = edge.Id;
                edge.Id = Guid.NewGuid();

                // Обновляем ссылки на вершины
                if (vertexIdMap.TryGetValue(edge.StartVertexId, out var newStartId))
                    edge.StartVertexId = newStartId;
                if (vertexIdMap.TryGetValue(edge.EndVertexId, out var newEndId))
                    edge.EndVertexId = newEndId;

                // Очищаем список транспортных средств
                edge.Vehicles.Clear();

                // Добавляем ребро в списки вершин
                var startVertex = clone.Vertices.FirstOrDefault(v => v.Id == edge.StartVertexId);
                var endVertex = clone.Vertices.FirstOrDefault(v => v.Id == edge.EndVertexId);

                if (startVertex != null)
                    startVertex.OutgoingEdges.Add(edge.Id);
                if (endVertex != null)
                    endVertex.IncomingEdges.Add(edge.Id);
            }
        }

        public async Task<bool> NetworkExistsAsync(Guid id)
        {
            var filePath = Path.Combine(_storagePath, $"{id}.json");
            return File.Exists(filePath);
        }

        public async Task<IEnumerable<NetworkIndexEntry>> GetNetworkIndexAsync()
        {
            lock (_lockObject)
            {
                var indexFile = Path.Combine(_storagePath, "_index.json");
                if (!File.Exists(indexFile))
                    return new List<NetworkIndexEntry>();

                var indexJson = File.ReadAllText(indexFile);
                return JsonConvert.DeserializeObject<List<NetworkIndexEntry>>(indexJson) ?? new();
            }
        }
    }

    public class NetworkIndexEntry
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public int VertexCount { get; set; }
        public int EdgeCount { get; set; }
    }

    // Класс для игнорирования навигационных свойств при сериализации
    public class IgnoreNavigationPropertiesResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
    {
        protected override IList<Newtonsoft.Json.Serialization.JsonProperty> CreateProperties(Type type, Newtonsoft.Json.MemberSerialization memberSerialization)
        {
            var properties = base.CreateProperties(type, memberSerialization);

            // Игнорируем навигационные свойства для избежания циклических ссылок
            var propertiesToIgnore = new[]
            {
                nameof(RoadSegment.StartVertex),
                nameof(RoadSegment.EndVertex),
                nameof(RoadSegment.Vehicles)
            };

            return properties
                .Where(p => !propertiesToIgnore.Contains(p.PropertyName))
                .ToList();
        }
    }
}