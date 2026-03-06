from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    # LLM provider: "gemini" or "openai" (vLLM uses OpenAI-compatible API)
    llm_provider: str = "gemini"
    google_api_key: str = ""
    openai_api_key: str = ""
    openai_base_url: str = ""
    model_name: str = "gemini-3.1-flash-preview"
    temperature: float = 1.0

    # Unity
    unity_server_url: str = "http://localhost:8765"

    # Operational
    max_steps: int = 50
    data_dir: str = "data"

    # Default scene
    default_scene: str = "ball_picker"

    model_config = {"env_file": ".env", "env_file_encoding": "utf-8"}


settings = Settings()
