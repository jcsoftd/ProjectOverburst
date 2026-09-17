public interface IItemUseHandler
{
    bool CanHandle(ItemData item);
    ItemUseResult CanUse(ItemUseContext context);
    ItemUseResult Use(ItemUseContext context);
}
