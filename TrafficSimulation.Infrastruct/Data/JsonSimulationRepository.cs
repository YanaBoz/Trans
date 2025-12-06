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
    public class JsonSimulationRepository : ISimulationRepository
    {
        private readonly string _storagePath;
        private readonly object _lockObject = new object();

        public JsonSimulationRepository(string storagePath = "Data/Simulations")
        {
            _storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, storagePath);
            Directory.CreateDirectory(_storagePath);
        }

        public async Task<SimulationSession> GetSessionAsync(Guid sessionId)
        {
            var filePath = Path.Combine(_storagePath, $"{sessionId}.json");
            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var session = JsonConvert.DeserializeObject<SimulationSession>(json,
                    new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        NullValueHandling = NullValueHandling.Ignore
                    });

                return session;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading session {sessionId}: {ex.Message}");
                return null;
            }
        }

        public async Task<IEnumerable<SimulationSession>> GetSessionsByNetworkAsync(Guid networkId)
        {
            lock (_lockObject)
            {
                var allFiles = Directory.GetFiles(_storagePath, "*.json");
                var sessions = new List<SimulationSession>();

                foreach (var file in allFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var session = JsonConvert.DeserializeObject<SimulationSession>(json,
                            new JsonSerializerSettings
                            {
                                TypeNameHandling = TypeNameHandling.Auto,
                                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                                NullValueHandling = NullValueHandling.Ignore
                            });

                        if (session != null && session.NetworkId == networkId)
                            sessions.Add(session);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading session from {file}: {ex.Message}");
                    }
                }

                return sessions.OrderByDescending(s => s.StartTime).ToList();
            }
        }

        public async Task SaveSessionAsync(SimulationSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            lock (_lockObject)
            {
                session.UpdateModifiedDate();
                session.IsSaved = true;

                var filePath = Path.Combine(_storagePath, $"{session.Id}.json");
                var json = JsonConvert.SerializeObject(session, Newtonsoft.Json.Formatting.Indented,
                    new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        NullValueHandling = NullValueHandling.Ignore,
                        ContractResolver = new IgnoreSessionPropertiesResolver()
                    });

                File.WriteAllText(filePath, json);
            }
        }

        public async Task DeleteSessionAsync(Guid sessionId)
        {
            lock (_lockObject)
            {
                var filePath = Path.Combine(_storagePath, $"{sessionId}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }

        public async Task<IEnumerable<SimulationSession>> GetAllSessionsAsync()
        {
            lock (_lockObject)
            {
                var allFiles = Directory.GetFiles(_storagePath, "*.json");
                var sessions = new List<SimulationSession>();

                foreach (var file in allFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var session = JsonConvert.DeserializeObject<SimulationSession>(json,
                            new JsonSerializerSettings
                            {
                                TypeNameHandling = TypeNameHandling.Auto,
                                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                                NullValueHandling = NullValueHandling.Ignore
                            });

                        if (session != null)
                            sessions.Add(session);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading session from {file}: {ex.Message}");
                    }
                }

                return sessions.OrderByDescending(s => s.StartTime).ToList();
            }
        }

        public async Task<bool> SessionExistsAsync(Guid sessionId)
        {
            var filePath = Path.Combine(_storagePath, $"{sessionId}.json");
            return File.Exists(filePath);
        }

        public async Task<IEnumerable<SimulationMetric>> GetSessionMetricsAsync(Guid sessionId)
        {
            var session = await GetSessionAsync(sessionId);
            return session?.Metrics ?? new List<SimulationMetric>();
        }

        public async Task ClearAllSessionsAsync()
        {
            lock (_lockObject)
            {
                var allFiles = Directory.GetFiles(_storagePath, "*.json");
                foreach (var file in allFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error deleting file {file}: {ex.Message}");
                    }
                }
            }
        }

        public async Task<SimulationSession> CloneSessionAsync(SimulationSession session, string newName)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new IgnoreSessionPropertiesResolver()
            };

            var json = JsonConvert.SerializeObject(session, settings);
            var clone = JsonConvert.DeserializeObject<SimulationSession>(json, settings);

            if (clone == null)
                throw new InvalidOperationException("Failed to clone session");

            clone.Id = Guid.NewGuid();
            clone.Name = newName ?? $"{session.Name} - Copy";
            clone.CreatedDate = DateTime.Now;
            clone.ModifiedDate = DateTime.Now;
            clone.StartTime = DateTime.Now;
            clone.EndTime = null;
            clone.IsSaved = true;

            // Сбрасываем статистику для новой сессии
            clone.CurrentTime = 0;
            clone.StepCount = 0;
            clone.CompletedVehiclesCount = 0;
            clone.CompletedPedestriansCount = 0;
            clone.State = Core.Models.SimulationState.Stopped;
            clone.Vehicles.Clear();
            clone.Pedestrians.Clear();
            clone.Metrics.Clear();
            clone.Incidents.Clear();
            clone.EdgeDensities.Clear();

            await SaveSessionAsync(clone);
            return clone;
        }
    }

    // Класс для игнорирования свойств при сериализации сессий
    public class IgnoreSessionPropertiesResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
    {
        protected override IList<Newtonsoft.Json.Serialization.JsonProperty> CreateProperties(Type type, Newtonsoft.Json.MemberSerialization memberSerialization)
        {
            var properties = base.CreateProperties(type, memberSerialization);

            // Для сессии мы можем сохранять все основные свойства
            return properties;
        }
    }
}