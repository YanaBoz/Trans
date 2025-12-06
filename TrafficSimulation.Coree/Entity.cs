namespace TrafficSimulation.Core
{
    public abstract class Entity
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        public Entity()
        {
            Id = Guid.NewGuid();
            CreatedDate = DateTime.Now;
            ModifiedDate = DateTime.Now;
        }

        public Entity(string name) : this()
        {
            Name = name;
        }

        public virtual void UpdateModifiedDate()
        {
            ModifiedDate = DateTime.Now;
        }
    }
}