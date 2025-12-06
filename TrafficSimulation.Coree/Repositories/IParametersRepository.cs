using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrafficSimulation.Core.Models;

namespace TrafficSimulation.Core.Repositories
{
    public interface IParametersRepository
    {
        Task<SimulationParameters> GetParametersAsync(Guid id);
        Task<IEnumerable<SimulationParameters>> GetAllParametersAsync();
        Task SaveParametersAsync(SimulationParameters parameters);
        Task DeleteParametersAsync(Guid id);
        Task<SimulationParameters> CreateDefaultParametersAsync(string v);
    }
}
