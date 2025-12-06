namespace TrafficSimulation.Core
{
    public static class MathHelpers
    {
        private static readonly Random _random = new();

        public static double RandomNormal(double mean, double stdDev)
        {
            double u1 = 1.0 - _random.NextDouble();
            double u2 = 1.0 - _random.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) *
                                   Math.Sin(2.0 * Math.PI * u2);
            return mean + stdDev * randStdNormal;
        }

        public static double RandomLogNormal(double mean, double stdDev)
        {
            double normal = RandomNormal(mean, stdDev);
            return Math.Exp(normal);
        }

        public static double RandomExponential(double mean)
        {
            return -mean * Math.Log(1.0 - _random.NextDouble());
        }

        public static T WeightedRandom<T>(Dictionary<T, double> distribution)
        {
            var totalWeight = distribution.Values.Sum();
            var randomValue = _random.NextDouble() * totalWeight;

            foreach (var (key, weight) in distribution)
            {
                randomValue -= weight;
                if (randomValue <= 0)
                    return key;
            }

            return distribution.Keys.First();
        }

        public static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        public static T GetRandomElement<T>(IList<T> list)
        {
            if (list == null || list.Count == 0)
                return default;
            return list[_random.Next(list.Count)];
        }
    }
}