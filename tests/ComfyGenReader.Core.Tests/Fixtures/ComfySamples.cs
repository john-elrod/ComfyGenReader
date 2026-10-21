namespace ComfyGenReader.Core.Tests;

internal static class ComfySamples
{
    public const string ApiPrompt = """
    {
      "3": {"class_type": "KSampler", "inputs": {"seed": 156680208700286, "steps": 20, "cfg": 8, "sampler_name": "euler", "scheduler": "normal", "denoise": 1,
        "model": ["4", 0], "positive": ["6", 0], "negative": ["7", 0], "latent_image": ["5", 0]}},
      "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "model.safetensors"}},
      "5": {"class_type": "EmptyLatentImage", "inputs": {"width": 512, "height": 512, "batch_size": 1}},
      "6": {"class_type": "CLIPTextEncode", "inputs": {"text": "a cat\nwearing a hat", "clip": ["4", 1]}},
      "7": {"class_type": "CLIPTextEncode", "inputs": {"text": "blurry", "clip": ["4", 1]}}
    }
    """;

    public const string Workflow = """
    {
      "last_node_id": 7,
      "last_link_id": 6,
      "nodes": [
        {"id": 4, "type": "CheckpointLoaderSimple", "widgets_values": ["model.safetensors"]},
        {"id": 5, "type": "EmptyLatentImage", "widgets_values": [512, 512, 1]},
        {"id": 6, "type": "CLIPTextEncode", "widgets_values": ["a cat\nwearing a hat"]},
        {"id": 7, "type": "CLIPTextEncode", "widgets_values": ["blurry"]},
        {"id": 3, "type": "KSampler", "widgets_values": [156680208700286, "randomize", 20, 8, "euler", "normal", 1]}
      ],
      "links": [[1, 4, 0, 3, 0, "MODEL"], [2, 6, 0, 3, 1, "CONDITIONING"], [3, 7, 0, 3, 2, "CONDITIONING"], [4, 5, 0, 3, 3, "LATENT"]],
      "version": 0.4
    }
    """;
}
