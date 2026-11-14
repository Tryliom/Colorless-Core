using System;

/**
 * Used to mark fields or properties that should be saved when passing from a scene/room to another
 * Should not be readonly
 */
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class SaveDataAttribute : Attribute {}