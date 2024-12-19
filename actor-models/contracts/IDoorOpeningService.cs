namespace actor_models.contracts;

public interface IDoorOpeningService: IGrainWithStringKey
{
    Task OpenDoor(string doorId);
}